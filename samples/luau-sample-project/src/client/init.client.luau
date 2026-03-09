-- Client entry point: Initializes the client-side UI and input handling
-- Connects to server events and manages local state

local ReplicatedStorage = game:GetService("ReplicatedStorage")
local Players = game:GetService("Players")
local UserInputService = game:GetService("UserInputService")

local function setupUI()
    local player = Players.LocalPlayer
    local playerGui = player:WaitForChild("PlayerGui")
    print("UI initialized for " .. player.Name)
end

local function connectEvents()
    local player = Players.LocalPlayer
    player.CharacterAdded:Connect(function(character)
        print("Character spawned")
    end)
end

local function handleInput(input: InputObject, gameProcessed: boolean)
    if gameProcessed then
        return
    end
    if input.UserInputType == Enum.UserInputType.MouseButton1 then
        print("Attack input received")
    end
end

setupUI()
connectEvents()
UserInputService.InputBegan:Connect(handleInput)
