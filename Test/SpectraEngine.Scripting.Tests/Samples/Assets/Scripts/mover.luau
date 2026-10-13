--!strict
-- A mover of a few lines: the player floats where it is pushed, and a dash
-- on sprint throws it ahead. Everything a mover is handed, used once.
local Players = game:GetService("Players")

local DASHED_AT = 1

local function float(
	state: CharacterState,
	command: CharacterCommand,
	collision: CharacterCollision,
	tuning: CharacterTuning,
	dt: number
)
	local velocity: Vector3 = command.MoveDirection * tuning.WalkSpeed
	if command.SprintPressed and state:GetMemory(DASHED_AT) == 0 then
		velocity *= tuning.SprintMultiplier * 4
		state:SetMemory(DASHED_AT, 30)
	elseif state:GetMemory(DASHED_AT) > 0 then
		state:SetMemory(DASHED_AT, state:GetMemory(DASHED_AT) - 1)
	end
	if command.Jump or command.JumpPressed then
		velocity += Vector3.new(0, tuning.JumpVelocity, 0)
	elseif command.Crouch or command.CrouchPressed then
		velocity += tuning.Gravity * 0.25
	end

	collision:ClearPlanes()
	local went: number, normal: Vector3, gap: number, point: Vector3, part: Instance? =
		collision:Sweep(state.Position, velocity * dt)
	if went < 1 then
		collision:AddPlane(normal)
		velocity = collision:Clip(velocity)
		if part and gap <= tuning.SkinWidth * 2 and point.Y > state.Position.Y then
			state:AddSlideContact(part)
		end
	end

	local position = state.Position + velocity * dt * went
	if collision:Gather(position, 0) > 0 then
		position += collision:Solve(Vector3.zero)
	end

	local ground: Vector3? = nil
	local stoodOn: Instance? = nil
	for index = 1, collision:Gather(position, tuning.SkinWidth * 2) do
		local facing: Vector3, _: number, under: Instance? = collision:Plane(index)
		if facing.Y >= tuning.MinGroundNormalY then
			ground, stoodOn = facing, under
		end
	end

	if ground then
		state:StandOn(ground, stoodOn)
		state.AirTicks = 0
	else
		state:LeaveGround()
		state.AirTicks = math.min(state.AirTicks + 1, 255)
	end

	state.Position = position
	state.Velocity = velocity
	state.SteppedUpBy = 0
	state.JumpBufferTicks = tuning.JumpBufferTicks
	state.GroundSuppressTicks = tuning.GroundSuppressTicks

	local standing: boolean = state.IsGrounded and state.GroundNormal.Y > 0 and state.GroundPart ~= nil
	local looking: number = command.Yaw + command.Pitch + command.MoveForward + command.MoveStrafe
	local used: boolean = command.Use or command.UsePressed or command.Sprint
	local size: number = tuning.Radius + tuning.StandHeight + tuning.EyeHeight + tuning.StepHeight
	local feel: number = tuning.GroundAcceleration + tuning.AirAcceleration + tuning.AirSpeedCap
		+ tuning.GroundFriction + tuning.StopSpeed + tuning.MaxFallSpeed + tuning.JumpHeight
	local limits: number = tuning.CoyoteTicks + tuning.GroundSnapDistance + tuning.MaxSlideIterations
		+ tuning.MaxDepenetrationIterations + tuning.MaxPushPerTick + collision.PlaneCount
	if standing and used and looking + size + feel + limits < 0 then
		print("never")
	end
end

local player = Players.LocalPlayer
local character = player and player.Character
if character then
	character:SetMover(float)
	character:SetMover(nil)
end
