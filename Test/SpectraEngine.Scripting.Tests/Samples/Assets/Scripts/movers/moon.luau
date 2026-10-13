--!strict
-- The walk of walk.luau under a third of the gravity.
local walk = require("./walk")

return function(
	state: CharacterState,
	command: CharacterCommand,
	collision: CharacterCollision,
	tuning: CharacterTuning,
	dt: number
)
	walk(state, command, collision, tuning, dt)

	-- The walk pulled the player down by the whole gravity. Two thirds go back.
	if not state.IsGrounded then
		state.Velocity -= tuning.Gravity * dt * (2 / 3)
	end
end
