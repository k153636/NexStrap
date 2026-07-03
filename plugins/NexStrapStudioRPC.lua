--!strict

-- NexStrap Studio RPC plugin.
-- Sends a structured Studio snapshot to the local NexStrap app.

local HttpService = game:GetService("HttpService")
local MarketplaceService = game:GetService("MarketplaceService")
local RunService = game:GetService("RunService")
local ScriptEditorService = game:GetService("ScriptEditorService")
local Selection = game:GetService("Selection")
local StudioService = game:GetService("StudioService")

local VERSION = "2.2.0"
local ENDPOINT = "http://localhost:4876/rpc"
local HTTP_TIMEOUT = 2
local POLL_INTERVAL = 0.5
local UPDATE_INTERVAL = 10
local COOLDOWN_TIME = 1.5

type StudioSnapshot = {
	name: string,
	placeId: number,
	isPublic: boolean,
}

type Payload = {
	details: string,
	context: string,
	mode: string,
	testing: boolean,
	placeId: number,
	isPublic: boolean,
	version: string,
	workspace: string,
	activeScript: string?,
	selectionCount: number,
	openDocuments: number,
}

type RpcEnvelope = {
	command: string,
	data: { [string]: unknown },
}

local enabled = plugin ~= nil
local onCooldown = false
local startupInitialized = false
local lastPayload: Payload? = nil
local studioSnapshot: StudioSnapshot = {
	name = "Unsaved Project",
	placeId = 0,
	isPublic = false,
}
local connections: { RBXScriptConnection } = {}

local function truncate(text: string, maxLength: number): string
	if #text <= maxLength then
		return text
	end
	if maxLength <= 3 then
		return string.sub(text, 1, maxLength)
	end
	return string.sub(text, 1, maxLength - 3) .. "..."
end

local function labelFromInstance(instance: Instance?): string?
	if instance == nil then
		return nil
	end

	local ok, fullName = pcall(function()
		return instance:GetFullName()
	end)

	if ok and typeof(fullName) == "string" and #fullName > 0 then
		if string.sub(fullName, 1, 5) == "game." then
			fullName = string.sub(fullName, 6)
		end
		return truncate(fullName, 80)
	end

	if instance.Name ~= "" then
		return truncate(instance.Name, 48)
	end

	return instance.ClassName
end

local function fetchStudioSnapshot(): StudioSnapshot
	local placeId = game.PlaceId

	if placeId > 0 then
		local ok, result = pcall(function()
			return MarketplaceService:GetProductInfoAsync(placeId)
		end)
		if ok and typeof(result) == "table" then
			local info = result :: { Name: string? }
			local name = info.Name
			if name and #name > 0 then
				return {
					name = name,
					placeId = placeId,
					isPublic = true,
				}
			end
		end
	end

	local raw = game.Name
	local name = (raw ~= "" and raw ~= "Place") and raw or "Unsaved Project"
	return {
		name = name,
		placeId = placeId,
		isPublic = false,
	}
end

local function getSelectionSummary(): (number, string?)
	local selected = Selection:Get()
	local count = #selected
	if count == 0 then
		return 0, nil
	end

	local names = table.create(math.min(count, 3))
	for i = 1, math.min(count, 3) do
		names[i] = labelFromInstance(selected[i]) or selected[i].ClassName
	end

	local suffix = ""
	if count > 3 then
		suffix = string.format(" +%d", count - 3)
	end

	return count, truncate(table.concat(names, ", ") .. suffix, 80)
end

local function getOpenDocumentCount(): number
	local ok, docs = pcall(function()
		return ScriptEditorService:GetScriptDocuments()
	end)
	if not ok or typeof(docs) ~= "table" then
		return 0
	end
	return #docs
end

local function buildDetails(): Payload
	studioSnapshot = fetchStudioSnapshot()

	local activeScript = labelFromInstance(StudioService.ActiveScript)
	local selectionCount, selectionLabel = getSelectionSummary()
	local openDocuments = getOpenDocumentCount()
	local testing = RunService:IsRunning()

	local mode = "Idle"
	local details = "Studio"
	local action = "Idle"
	local target = "No active context"
	local modeSource = "fallback"
	local contextParts = {
		"Project: " .. studioSnapshot.name,
		"PlaceId: " .. tostring(studioSnapshot.placeId),
		"Visibility: " .. (studioSnapshot.isPublic and "Public" or "Private"),
		"Script: None",
		"Selection: None",
		"Open docs: 0",
		"Mode source: fallback",
	}

	if testing then
		mode = "Testing"
		action = "Testing"
		modeSource = "RunService:IsRunning()"
		if activeScript ~= nil then
			target = "Script: " .. activeScript
		elseif selectionLabel ~= nil then
			target = "Selection: " .. selectionLabel
		elseif selectionCount > 0 then
			target = string.format("Selection: %d selected", selectionCount)
		else
			target = string.format("Open docs: %d", openDocuments)
		end
	elseif activeScript ~= nil then
		mode = "Editing"
		action = "Editing"
		modeSource = "StudioService.ActiveScript"
		target = "Script: " .. activeScript
	elseif selectionCount > 0 then
		mode = "Selecting"
		action = "Selecting"
		modeSource = "Selection:Get()"
		target = selectionLabel ~= nil and ("Selection: " .. selectionLabel) or string.format("Selection: %d selected", selectionCount)
	elseif openDocuments > 0 then
		mode = "Browsing"
		action = "Browsing"
		modeSource = "ScriptEditorService:GetScriptDocuments()"
		target = string.format("Open docs: %d", openDocuments)
	end

	contextParts[4] = "Script: " .. (activeScript or "None")
	contextParts[5] = selectionLabel ~= nil and ("Selection: " .. selectionLabel) or string.format("Selection: %d selected", selectionCount)
	contextParts[6] = "Open docs: " .. tostring(openDocuments)
	contextParts[7] = "Mode source: " .. modeSource

	details = truncate(action .. " - " .. target, 120)

	return {
		details = details,
		context = truncate(table.concat(contextParts, " | "), 160),
		mode = mode,
		testing = testing,
		placeId = studioSnapshot.placeId,
		isPublic = studioSnapshot.isPublic,
		version = VERSION,
		workspace = studioSnapshot.name,
		activeScript = activeScript,
		selectionCount = selectionCount,
		openDocuments = openDocuments,
	}
end

local function send(command: string, data: { [string]: unknown }): ()
	local envelope: RpcEnvelope = {
		command = command,
		data = data,
	}
	local body = HttpService:JSONEncode(envelope)

	task.spawn(function()
		pcall(function()
			HttpService:RequestAsync({
				Url = ENDPOINT,
				Method = "POST",
				Headers = { ["Content-Type"] = "application/json" },
				Body = body,
				Timeout = HTTP_TIMEOUT,
			})
		end)
	end)
end

local function payloadsEqual(a: Payload, b: Payload): boolean
	return a.details == b.details
		and a.context == b.context
		and a.mode == b.mode
		and a.testing == b.testing
		and a.placeId == b.placeId
		and a.isPublic == b.isPublic
		and a.workspace == b.workspace
		and a.activeScript == b.activeScript
		and a.selectionCount == b.selectionCount
		and a.openDocuments == b.openDocuments
end

local function updatePresence(force: boolean?, isStartup: boolean?): ()
	if not enabled then
		return
	end
	if onCooldown and not force then
		return
	end

	local payload = buildDetails()

	if not force then
		local last = lastPayload
		if last ~= nil and payloadsEqual(payload, last) then
			return
		end
	end

	lastPayload = payload
	onCooldown = true
	task.delay(COOLDOWN_TIME, function()
		onCooldown = false
	end)

	if isStartup and not startupInitialized then
		startupInitialized = true
		send("Initialize", payload :: { [string]: unknown })
	else
		send("SetRichPresence", payload :: { [string]: unknown })
	end
end

local function refreshStudioThenUpdate(isStartup: boolean?): ()
	task.spawn(function()
		updatePresence(true, isStartup)
	end)
end

local function disconnect(): ()
	for _, connection in connections do
		connection:Disconnect()
	end
	table.clear(connections)
end

local function shutdown(): ()
	enabled = false
	send("RPCToggle", {
		enabled = false,
		workspace = studioSnapshot.name,
		isPublic = studioSnapshot.isPublic,
		details = lastPayload and lastPayload.details or "Studio",
		context = lastPayload and lastPayload.context or "",
		mode = lastPayload and lastPayload.mode or "Idle",
	})
	disconnect()
end

local function wire(): ()
	table.insert(connections, Selection.SelectionChanged:Connect(function()
		task.defer(updatePresence)
	end))

	table.insert(connections, StudioService:GetPropertyChangedSignal("ActiveScript"):Connect(function()
		task.defer(updatePresence)
	end))

	task.spawn(function()
		local prev = RunService:IsRunning()
		while enabled do
			task.wait(POLL_INTERVAL)
			local cur = RunService:IsRunning()
			if cur ~= prev then
				prev = cur
				updatePresence(true, false)
			end
		end
	end)

	table.insert(connections, game:GetPropertyChangedSignal("PlaceId"):Connect(function()
		task.delay(1, function()
			refreshStudioThenUpdate(false)
		end)
	end))

	if game:IsLoaded() then
		refreshStudioThenUpdate(true)
	else
		table.insert(connections, game.Loaded:Connect(function()
			task.delay(0.5, function()
				refreshStudioThenUpdate(true)
			end)
		end))
	end

	task.spawn(function()
		while enabled do
			task.wait(UPDATE_INTERVAL)
			if enabled then
				updatePresence()
			end
		end
	end)
end

if not plugin then
	return
end

local toolbar = plugin:CreateToolbar("NexStrap")
local button = toolbar:CreateButton(
	"Toggle RPC",
	"Toggle the NexStrap Studio RPC bridge.",
	"rbxassetid://111400040119373"
)
button:SetActive(true)

button.Click:Connect(function()
	enabled = not enabled
	button:SetActive(enabled)

	send("RPCToggle", {
		enabled = enabled,
		workspace = studioSnapshot.name,
		isPublic = studioSnapshot.isPublic,
		details = lastPayload and lastPayload.details or "Studio",
		context = lastPayload and lastPayload.context or "",
		mode = lastPayload and lastPayload.mode or "Idle",
	})

	if enabled then
		refreshStudioThenUpdate(startupInitialized == false)
	end
end)

plugin.Unloading:Connect(shutdown)

wire()
