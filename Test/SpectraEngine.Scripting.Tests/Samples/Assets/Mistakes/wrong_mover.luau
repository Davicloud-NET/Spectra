--!strict
-- One mistake a line. After each arrow is what the checker has to say of it.
local player = game:GetService("Players").LocalPlayer :: Player
local character = player.Character :: Character

character:SetMover(function(state, command, collision, tuning, dt)
	state.Position = 5 --> 'Vector3', but got 'number'
	state.IsGrounded = true --> new: IsGrounded of type 'CharacterState' is read-only
	state:StandOn() --> but only 1 is specified
	state:SetMemory("jumps", 1) --> 'number', but got 'string'
	command.Jump = true --> new: Jump of type 'CharacterCommand' is read-only
	print(command.Buttons) --> Buttons
	tuning.WalkSpeed = 9 --> new: WalkSpeed of type 'CharacterTuning' is read-only
	print(tuning.Speed) --> Speed
	local went: Vector3 = collision:Sweep(state.Position, command.MoveDirection * dt) --> 'Vector3', but got 'number'
	collision:AddPlane(went, 0) --> expects 2 arguments, but 3 are specified
	print(collision:Plane("first")) --> 'number', but got 'string'
	return state --> new: '()', but got 'CharacterState'
end)

character:SetMover(function(state: Character) end) --> CharacterState
character:SetMover("walk") --> but got 'string'
character.SetMover = nil --> but got 'nil'
