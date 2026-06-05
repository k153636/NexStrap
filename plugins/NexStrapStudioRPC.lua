--!strict
-- NexStrap Studio RPC Plugin v2.0.0
-- Sends Studio presence data to NexStrap Launcher via HTTP on port 4876.
-- Requires "Allow HTTP Requests" in Experience Settings → Security.

-- ── サービス ──────────────────────────────────────────────────────────────

local Selection           = game:GetService("Selection")
local RunService          = game:GetService("RunService")
local HttpService         = game:GetService("HttpService")
local MarketplaceService  = game:GetService("MarketplaceService")
local StudioService       = game:GetService("StudioService")
local ScriptEditorService = game:GetService("ScriptEditorService")

-- ── 定数 ──────────────────────────────────────────────────────────────────

local VERSION         = "2.0.0"
local PORT            = 4876
local HTTP_TIMEOUT    = 2
local UPDATE_INTERVAL = 10   -- 定期送信間隔（秒）
local COOLDOWN        = 1.5  -- 連続送信の最小間隔（秒）

-- ── 型定義 ────────────────────────────────────────────────────────────────

type WorkspaceInfo = {
    name     : string,
    placeId  : number,
    isPublic : boolean,
}

type PresencePayload = {
    details  : string?,
    testing  : boolean,
    placeId  : number,
    isPublic : boolean,
    version  : string,
}

-- ── モジュール状態 ────────────────────────────────────────────────────────

local enabled         : boolean        = true
local initialized     : boolean        = false
local cooldownActive  : boolean        = false
local lastPayload     : PresencePayload? = nil
local monitorHandle   : thread?        = nil
local connections     : { RBXScriptConnection } = {}
local workspace_cache : WorkspaceInfo  = { name = "Unsaved Project", placeId = 0, isPublic = false }

-- ── ワークスペース情報 ────────────────────────────────────────────────────

local function fetchWorkspaceInfo(): WorkspaceInfo
    local placeId = game.PlaceId

    if placeId > 0 then
        local ok, info = pcall(function()
            return MarketplaceService:GetProductInfoAsync(placeId)
        end)
        if ok and info and info.Name then
            return { name = info.Name, placeId = placeId, isPublic = true }
        end
    end

    local name = game.Name
    local displayName = (name ~= "" and name ~= "Place") and name or "Unsaved Project"
    return { name = displayName, placeId = placeId, isPublic = false }
end

local function refreshWorkspaceAsync()
    task.spawn(function()
        local info = fetchWorkspaceInfo()
        workspace_cache = info
    end)
end

-- ── HTTP 送信 ─────────────────────────────────────────────────────────────

local function post(command: string, data: { [string]: any })
    if not HttpService.HttpEnabled then
        warn("[NexStrap] HTTP Requests が無効です。Experience Settings → Security で有効にしてください。")
        return
    end

    local body = HttpService:JSONEncode({ command = command, data = data })

    task.spawn(function()
        local ok, err = pcall(function()
            HttpService:RequestAsync({
                Url     = string.format("http://localhost:%d/rpc", PORT),
                Method  = "POST",
                Headers = { ["Content-Type"] = "application/json" },
                Body    = body,
                Timeout = HTTP_TIMEOUT,
            })
        end)
        if not ok then
            -- 接続失敗はサイレントに無視（ランチャーが起動していない場合）
        end
    end)
end

-- ── Presence 計算 ─────────────────────────────────────────────────────────

local function buildPayload(): PresencePayload
    return {
        details  = workspace_cache.name,
        testing  = RunService:IsRunning(),
        placeId  = workspace_cache.placeId,
        isPublic = workspace_cache.isPublic,
        version  = VERSION,
    }
end

local function payloadsEqual(a: PresencePayload, b: PresencePayload): boolean
    return a.details  == b.details
        and a.testing  == b.testing
        and a.placeId  == b.placeId
        and a.isPublic == b.isPublic
end

-- ── Presence 送信 ────────────────────────────────────────────────────────

local function sendPresence(force: boolean?)
    if not enabled then return end
    if cooldownActive and not force then return end

    local payload = buildPayload()

    if not force and lastPayload and payloadsEqual(payload, lastPayload) then return end

    lastPayload      = payload
    cooldownActive   = true

    post("SetRichPresence", payload :: { [string]: any })

    task.delay(COOLDOWN, function()
        cooldownActive = false
    end)
end

-- ── 有効/無効 ─────────────────────────────────────────────────────────────

local function startMonitor()
    if monitorHandle then return end
    monitorHandle = task.spawn(function()
        while enabled do
            task.wait(UPDATE_INTERVAL)
            if enabled then sendPresence() end
        end
        monitorHandle = nil
    end)
end

local function stopMonitor()
    enabled       = false
    monitorHandle = nil
end

local function setEnabled(value: boolean)
    enabled = value
    post("RPCToggle", {
        enabled   = value,
        workspace = workspace_cache.name,
        isPublic  = workspace_cache.isPublic,
    })
    if value then
        refreshWorkspaceAsync()
        task.defer(function() sendPresence(true) end)
        startMonitor()
    else
        stopMonitor()
    end
end

-- ── イベント購読 ──────────────────────────────────────────────────────────

local function disconnectAll()
    for _, c in connections do
        c:Disconnect()
    end
    table.clear(connections)
end

local function subscribeEvents()
    -- スクリプト選択変化 → 即時送信
    table.insert(connections, Selection.SelectionChanged:Connect(function()
        task.defer(sendPresence)
    end))

    -- アクティブスクリプト変化 → 即時送信
    table.insert(connections, StudioService:GetPropertyChangedSignal("ActiveScript"):Connect(function()
        task.defer(sendPresence)
    end))

    -- テスト開始/終了 → 即時送信（強制）
    table.insert(connections, RunService:GetPropertyChangedSignal("IsRunning"):Connect(function()
        task.defer(function() sendPresence(true) end)
    end))

    -- テレポート・PlaceId 変化 → キャッシュ更新 + 再送
    table.insert(connections, game:GetPropertyChangedSignal("PlaceId"):Connect(function()
        refreshWorkspaceAsync()
        task.delay(1, function() sendPresence(true) end)
    end))

    -- ゲーム読み込み完了 → キャッシュ確定
    table.insert(connections, game.Loaded:Connect(function()
        refreshWorkspaceAsync()
        task.delay(0.5, function() sendPresence(true) end)
    end))
end

-- ── 初期化 ────────────────────────────────────────────────────────────────

local function initialize()
    if initialized or not plugin then return end
    initialized = true

    -- プラグインアクション（ツールバートグル）
    local toolbar = plugin:CreateToolbar("NexStrap")
    local button  = toolbar:CreateButton(
        "Toggle RPC",
        "Discord Rich Presence のオン/オフ",
        "rbxassetid://111400040119373"
    )
    button.Click:Connect(function()
        setEnabled(not enabled)
        button:SetActive(enabled)
    end)
    button:SetActive(enabled)

    -- プラグインアンロード時のクリーンアップ
    plugin.Unloading:Connect(function()
        setEnabled(false)
        disconnectAll()
    end)

    subscribeEvents()

    -- 起動時はゲームのロード完了を待ってから初回送信
    if game:IsLoaded() then
        refreshWorkspaceAsync()
        task.delay(0.5, function() sendPresence(true) end)
    end
    -- game.Loaded イベントでも送信される（subscribeEvents で登録済み）

    setEnabled(true)
    startMonitor()
end

initialize()
