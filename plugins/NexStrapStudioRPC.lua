-- NexStrap Studio RPC Plugin v1.0.0
-- Based on FroststrapStudioRPC (https://github.com/Froststrap/FroststrapStudioRPC)
-- Sends Studio presence data to NexStrap Launcher via HTTP on port 4876

local Selection          = game:GetService("Selection")
local RunService         = game:GetService("RunService")
local HttpService        = game:GetService("HttpService")
local MarketplaceService = game:GetService("MarketplaceService")
local Players            = game:GetService("Players")
local StudioService      = game:GetService("StudioService")
local ScriptEditorService = game:GetService("ScriptEditorService")

local Plugin = if plugin then plugin else nil

local NexStrapStudioRPC = {}

NexStrapStudioRPC.Config = {
    enabled        = true,
    httpTimeout    = 2,
    updateInterval = 10,
    port           = 4876,
}

local State = {
    lastPayload      = {},
    isCooldown       = false,
    isInitialized    = false,
    monitoringThread = nil,
    connections      = {},
    cachedWorkspace  = { name = "Unsaved Studio Project", isPublic = false },
}

local ScriptTypes = {
    SERVER        = "Server Script",
    CLIENT        = "Local Script",
    SERVER_MODULE = "Server Module",
    CLIENT_MODULE = "Client Module",
    MODULE        = "Module",
    DEVELOPING    = "Developing",
}

-- ── ヘルパー ───────────────────────────────────────────────────────────────

local function clearConnections()
    for _, connection in pairs(State.connections) do
        if connection then connection:Disconnect() end
    end
    table.clear(State.connections)
end

local function getScriptLineCount(scriptObj)
    if not scriptObj or not scriptObj:IsA("LuaSourceContainer") then return 0 end
    local ok, source = pcall(function() return ScriptEditorService:GetEditorSource(scriptObj) end)
    if not ok or not source then
        ok, source = pcall(function() return scriptObj.Source end)
    end
    if not ok or not source then return 0 end
    local _, n = source:gsub("\n", "")
    return n + 1
end

local function getScriptType(scriptObj)
    if not scriptObj then return ScriptTypes.DEVELOPING end

    if scriptObj:IsA("LocalScript")
    or (scriptObj:IsA("Script") and scriptObj.RunContext == Enum.RunContext.Client) then
        return ScriptTypes.CLIENT
    end

    if scriptObj:IsA("ModuleScript") then
        local a = scriptObj.Parent
        while a do
            if a:IsA("ServerScriptService") or a:IsA("ServerStorage") then
                return ScriptTypes.SERVER_MODULE
            elseif a:IsA("StarterPlayer") or a:IsA("StarterGui") or a:IsA("StarterPack") then
                return ScriptTypes.CLIENT_MODULE
            end
            a = a.Parent
        end
        return ScriptTypes.MODULE
    end

    if scriptObj:IsA("Script") then return ScriptTypes.SERVER end
    return ScriptTypes.DEVELOPING
end

local function refreshWorkspaceCache()
    if game.PlaceId > 0 then
        local ok, info = pcall(function() return MarketplaceService:GetProductInfoAsync(game.PlaceId) end)
        if ok and info then
            State.cachedWorkspace.name     = info.Name or "Published Place"
            State.cachedWorkspace.isPublic = true
            return
        end
    end
    local name = game.Name
    State.cachedWorkspace.name     = (name ~= "Place" and name ~= "") and name or "Unsaved Studio Project"
    State.cachedWorkspace.isPublic = false
end

-- ── HTTP 送信 ─────────────────────────────────────────────────────────────

local function sendViaHTTP(payload)
    if not HttpService.HttpEnabled then return end
    task.spawn(function()
        local url = string.format("http://localhost:%d/rpc", NexStrapStudioRPC.Config.port)
        pcall(function()
            HttpService:RequestAsync({
                Url     = url,
                Method  = "POST",
                Headers = { ["Content-Type"] = "application/json" },
                Body    = HttpService:JSONEncode(payload),
                Timeout = NexStrapStudioRPC.Config.httpTimeout,
            })
        end)
    end)
end

-- ── Presence 送信 ────────────────────────────────────────────────────────

function NexStrapStudioRPC.SendMessage(data)
    if State.isCooldown then return end

    -- 重複送信を防止
    local isRedundant = true
    for k, v in pairs(data) do
        if State.lastPayload[k] ~= v then isRedundant = false; break end
    end
    if isRedundant then return end

    State.lastPayload = data
    State.isCooldown  = true
    task.delay(2, function() State.isCooldown = false end)

    sendViaHTTP({ command = "SetRichPresence", data = data })
end

function NexStrapStudioRPC.UpdatePresence()
    if not NexStrapStudioRPC.Config.enabled then return end

    local activeScript = StudioService.ActiveScript
    local scriptObj = (activeScript and activeScript:IsA("LuaSourceContainer")) and activeScript or nil

    if not scriptObj then
        local selected = Selection:Get()
        if #selected == 1 and selected[1]:IsA("LuaSourceContainer") then
            scriptObj = selected[1]
        end
    end

    local scriptType = getScriptType(scriptObj)
    local stateText  = scriptObj
        and string.format("Editing %s (%d lines)", scriptObj.Name, getScriptLineCount(scriptObj))
        or "Idling in Studio"

    NexStrapStudioRPC.SendMessage({
        details    = State.cachedWorkspace.name,
        state      = stateText,
        testing    = RunService:IsRunning(),
        scriptType = scriptType,
        placeId    = game.PlaceId,
        isPublic   = State.cachedWorkspace.isPublic,
        devCount   = math.max(1, #Players:GetPlayers()),
    })
end

-- ── 有効/無効切り替え ─────────────────────────────────────────────────────

function NexStrapStudioRPC.SetEnabled(enabled)
    NexStrapStudioRPC.Config.enabled = enabled

    sendViaHTTP({
        command = "RPCToggle",
        data    = {
            enabled   = enabled,
            workspace = State.cachedWorkspace.name,
            isPublic  = State.cachedWorkspace.isPublic,
        },
    })

    if enabled then
        refreshWorkspaceCache()
        NexStrapStudioRPC.UpdatePresence()
        if not State.monitoringThread then
            State.monitoringThread = task.spawn(function()
                while NexStrapStudioRPC.Config.enabled do
                    task.wait(NexStrapStudioRPC.Config.updateInterval)
                    NexStrapStudioRPC.UpdatePresence()
                end
                State.monitoringThread = nil
            end)
        end
    else
        State.monitoringThread = nil
    end
end

-- ── 初期化 ────────────────────────────────────────────────────────────────

function NexStrapStudioRPC.Initialize()
    if State.isInitialized or not Plugin then return end

    local ok, enabled = pcall(function() return HttpService.HttpEnabled end)
    if ok and not enabled then
        warn("[NexStrap] HTTP Requests are disabled. Enable them in Experience Settings → Security to use Studio RPC.")
    end

    local toggleAction = Plugin:CreatePluginAction(
        "nexstrap_toggle_rpc",
        "Toggle NexStrap RPC",
        "Toggle Discord Rich Presence for NexStrap",
        "rbxassetid://111400040119373",
        true
    )

    State.connections.Toggle = toggleAction.Triggered:Connect(function()
        NexStrapStudioRPC.SetEnabled(not NexStrapStudioRPC.Config.enabled)
    end)

    State.connections.SelectionChanged = Selection.SelectionChanged:Connect(function()
        task.defer(NexStrapStudioRPC.UpdatePresence)
    end)

    State.connections.ActiveScriptChanged = StudioService:GetPropertyChangedSignal("ActiveScript"):Connect(function()
        task.defer(NexStrapStudioRPC.UpdatePresence)
    end)

    State.connections.Unload = Plugin.Unloading:Connect(function()
        NexStrapStudioRPC.SetEnabled(false)
        clearConnections()
    end)

    refreshWorkspaceCache()
    NexStrapStudioRPC.SetEnabled(NexStrapStudioRPC.Config.enabled)
    State.isInitialized = true
end

if Plugin then NexStrapStudioRPC.Initialize() end
return NexStrapStudioRPC
