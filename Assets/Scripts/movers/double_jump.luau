--!strict
-- The walk of walk.luau, with a second jump in the air.
local walk = require("./walk")

-- Where the state keeps how many jumps were made in the air. A mover keeps
-- no count of its own, or the same ticks played again would not jump alike.
local AIR_JUMPS = 1

return function(
	state: CharacterState,
	command: CharacterCommand,
	collision: CharacterCollision,
	tuning: CharacterTuning,
	dt: number
)
	if state.IsGrounded then
		state:SetMemory(AIR_JUMPS, 0)
	end

	-- In the air for longer than a late jump still counts, with a jump left.
	local jumpsAgain = command.JumpPressed
		and not state.IsGrounded
		and state.AirTicks > tuning.CoyoteTicks
		and state:GetMemory(AIR_JUMPS) < 1

	if jumpsAgain then
		local velocity = state.Velocity
		state.Velocity = Vector3.new(velocity.X, tuning.JumpVelocity, velocity.Z)
		state:SetMemory(AIR_JUMPS, 1)
	end

	walk(state, command, collision, tuning, dt)

	-- The press was this jump. Kept, it would jump again on landing.
	if jumpsAgain then
		state.JumpBufferTicks = 0
	end
end
