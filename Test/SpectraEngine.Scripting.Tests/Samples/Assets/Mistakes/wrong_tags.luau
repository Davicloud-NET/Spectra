--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local CollectionService = game:GetService("CollectionService")
local board = workspace:WaitForChild("Board")

board:AddTag(7) --> 'string', but got 'number'
board:RemoveTag(board) --> 'string', but got 'Instance'
board:HasTag() --> expects 2 arguments, but only 1 is specified
print(math.abs(board:GetTags()[1])) --> 'number', but got 'string'

board:SetAttribute(5, 1) --> 'string', but got 'number'
board:SetAttribute("Owner", workspace) --> but got 'Instance'
board:SetAttribute("Scores", { 1, 2 }) --> but got '{number}'
board:GetAttribute(board) --> 'string', but got 'Instance'
board.AttributeChanged:Connect(function(attribute: number) end) --> '(string) -> ()'
board:GetAttributeChangedSignal("Score"):Connect(function(score: number) end) --> '() -> ()', but got '(number) -> ()'
board:GetAttributeChangedSignal(7) --> 'string', but got 'number'
board.AttributeChanged = nil --> old: 'RBXScriptSignal<string>', but got 'nil' --> new: AttributeChanged of type 'Instance' is read-only

CollectionService:GetTagged(board) --> 'string', but got 'Instance'
CollectionService:AddTag("Open", board) --> 'Instance', but got 'string'
CollectionService:HasTag(board) --> expects 3 arguments, but only 2 are specified
CollectionService:GetInstanceAddedSignal("Lava"):Connect(function(part: string) end) --> '(Instance) -> ()'
CollectionService:GetInstanceRemovedSignal("Lava"):Fire(board) --> Key 'Fire' not found
CollectionService:GetTaged("Lava") --> GetTaged
