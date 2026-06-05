--!strict

--[[
    NexStrap Studio RPC Plugin  v2.1.0
    Discord Rich Presence integration for Roblox Studio.

    Requires: Experience Settings → Security → Allow HTTP Requests = ON
    Launcher:  NexStrap (localhost:4876)
--]]

-- ── Services ──────────────────────────────────────────────────────────────

local HttpService         = game:GetService("HttpService")
local MarketplaceService  = game:GetService("MarketplaceService")
local RunService          = game:GetService("RunService")
local ScriptEditorService = game:GetService("ScriptEditorService")
local Selection           = game:GetService("Selection")
local StudioService       = game:GetService("StudioService")

-- ── Constants ─────────────────────────────────────────────────────────────

local VERSION         : string = "2.1.0"
local ENDPOINT        : string = "http://localhost:4876/rpc"
local HTTP_TIMEOUT    : number = 2
local POLL_INTERVAL   : number = 0.5   -- IsRunning() ポーリング間隔（秒）
local UPDATE_INTERVAL : number = 10    -- 定期送信間隔（秒）
local COOLDOWN_TIME   : number = 1.5   -- 連続送信の最小間隔（秒）

-- ── Types ─────────────────────────────────────────────────────────────────

type WorkspaceInfo = {
    name     : string,
    placeId  : number,
    isPublic : boolean,
}

type Payload = {
    details  : string,
    testing  : boolean,
    placeId  : number,
    isPublic : boolean,
    version  : string,
}

type RpcEnvelope = {
    command : string,
    data    : { [string]: unknown },
}

-- ── State ─────────────────────────────────────────────────────────────────

local enabled       : boolean          = true
local onCooldown    : boolean          = false
local lastPayload   : Payload?         = nil
local workspace     : WorkspaceInfo    = { name = "Unsaved Project", placeId = 0, isPublic = false }
local connections   : { RBXScriptConnection } = {}

-- ── Workspace ─────────────────────────────────────────────────────────────

local function fetchWorkspace(): WorkspaceInfo
    local placeId = game.PlaceId

    if placeId > 0 then
        local ok, result = pcall(function()
            return MarketplaceService:GetProductInfoAsync(placeId)
        end)
        if ok and typeof(result) == "table" then
            local info = result :: { Name: string? }
            local name = info.Name
            if name and #name > 0 then
                return { name = name, placeId = placeId, isPublic = true }
            end
        end
    end

    local raw = game.Name
    local name = (raw ~= "" and raw ~= "Place") and raw or "Unsaved Project"
    return { name = name, placeId = placeId, isPublic = false }
end

-- ワークスペース情報を取得してから presence を送信する。
-- 先に updatePresence を呼ぶと API 取得前の仮名（"Unsaved Project" 等）が表示されるため
-- 必ず fetchWorkspace 完了後に送信する。
local function refreshWorkspaceThenUpdate(): ()
    task.spawn(function()
        workspace = fetchWorkspace()
        updatePresence(true)
    end)
end

-- ── HTTP ──────────────────────────────────────────────────────────────────

local function send(command: string, data: { [string]: unknown }): ()
    local envelope: RpcEnvelope = { command = command, data = data }
    local body = HttpService:JSONEncode(envelope)

    task.spawn(function()
        -- HTTP が無効 / ランチャー未起動はサイレントに無視（pcall で完全に捕捉）
        pcall(function()
            HttpService:RequestAsync({
                Url     = ENDPOINT,
                Method  = "POST",
                Headers = { ["Content-Type"] = "application/json" },
                Body    = body,
                Timeout = HTTP_TIMEOUT,
            })
        end)
    end)
end

-- ── Presence ──────────────────────────────────────────────────────────────

local function buildPayload(): Payload
    return {
        details  = workspace.name,
        testing  = RunService:IsRunning(),
        placeId  = workspace.placeId,
        isPublic = workspace.isPublic,
        version  = VERSION,
    }
end

local function payloadsEqual(a: Payload, b: Payload): boolean
    return a.details  == b.details
        and a.testing  == b.testing
        and a.placeId  == b.placeId
        and a.isPublic == b.isPublic
end

local function updatePresence(force: boolean?): ()
    if not enabled then return end
    if onCooldown and not force then return end

    local payload = buildPayload()

    if not force then
        local last = lastPayload
        if last ~= nil and payloadsEqual(payload, last) then return end
    end

    lastPayload = payload
    onCooldown  = true
    task.delay(COOLDOWN_TIME, function() onCooldown = false end)

    send("SetRichPresence", payload :: { [string]: unknown })
end

-- ── Teardown ──────────────────────────────────────────────────────────────

local function disconnect(): ()
    for _, c in connections do
        c:Disconnect()
    end
    table.clear(connections)
end

local function shutdown(): ()
    enabled = false
    send("RPCToggle", {
        enabled   = false,
        workspace = workspace.name,
        isPublic  = workspace.isPublic,
    })
    disconnect()
end

-- ── Event Wiring ──────────────────────────────────────────────────────────

local function wire(): ()
    -- スクリプト選択 / アクティブスクリプト切り替え
    table.insert(connections, Selection.SelectionChanged:Connect(function()
        task.defer(updatePresence)
    end))

    table.insert(connections, StudioService:GetPropertyChangedSignal("ActiveScript"):Connect(function()
        task.defer(updatePresence)
    end))

    -- テストプレイ開始 / 終了（IsRunning はイベントを持たないのでポーリング）
    task.spawn(function()
        local prev = RunService:IsRunning()
        while enabled do
            task.wait(POLL_INTERVAL)
            local cur = RunService:IsRunning()
            if cur ~= prev then
                prev = cur
                updatePresence(true)
            end
        end
    end)

    -- テレポート（PlaceId の変化）
    table.insert(connections, game:GetPropertyChangedSignal("PlaceId"):Connect(function()
        task.delay(1, refreshWorkspaceThenUpdate) -- テレポート後に PlaceId が確定してから取得
    end))

    -- ゲームロード完了
    if game:IsLoaded() then
        refreshWorkspaceThenUpdate()
    else
        table.insert(connections, game.Loaded:Connect(function()
            task.delay(0.5, refreshWorkspaceThenUpdate)
        end))
    end

    -- 定期送信（接続が切れた場合のフォールバック）
    task.spawn(function()
        while enabled do
            task.wait(UPDATE_INTERVAL)
            if enabled then updatePresence() end
        end
    end)
end

-- ── Plugin Entry ──────────────────────────────────────────────────────────

if not plugin then
    -- テスト環境（Studio 外）では何もしない
    return
end

-- ツールバーボタン
local toolbar = plugin:CreateToolbar("NexStrap")
local button  = toolbar:CreateButton(
    "Toggle RPC",
    "Discord Rich Presence のオン/オフ",
    "rbxassetid://111400040119373"
)
button:SetActive(true)

button.Click:Connect(function()
    enabled = not enabled
    button:SetActive(enabled)

    send("RPCToggle", {
        enabled   = enabled,
        workspace = workspace.name,
        isPublic  = workspace.isPublic,
    })

    if enabled then
        refreshWorkspaceThenUpdate()
    end
end)

plugin.Unloading:Connect(shutdown)

-- 初回起動（API 取得完了後に送信するため refreshWorkspaceThenUpdate を使う）
wire()
refreshWorkspaceThenUpdate()
