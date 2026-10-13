--!strict

-- How the player moves: walk, sprint, jump, slide along walls, step up. It
-- does what the engine's built-in mover does. Change a rule here and the
-- player moves another way on the next Play. It keeps nothing between ticks:
-- what it needs later it leaves in the state, so played again it moves alike.

local function dot(a: Vector3, b: Vector3): number
	return a.X * b.X + a.Y * b.Y + a.Z * b.Z
end

-- Adds only what is missing along the wish, so holding a direction never
-- passes the wished speed.
local function accelerate(velocity: Vector3, wish: Vector3, wishSpeed: number, rate: number, dt: number): Vector3
	if wishSpeed <= 0 or wish == Vector3.zero then
		return velocity
	end

	local missing = wishSpeed - dot(velocity, wish)
	if missing <= 0 then
		return velocity
	end

	return velocity + wish * math.min(rate * dt * wishSpeed, missing)
end

-- The velocity before anything is hit: the ground slows it, and the walk
-- the player asks for pushes it.
local function steer(state: CharacterState, command: CharacterCommand, tuning: CharacterTuning, dt: number): Vector3
	local velocity = state.Velocity
	local speed = tuning.WalkSpeed * (if command.Sprint then tuning.SprintMultiplier else 1)
	local wish = command.MoveDirection
	if not state.IsGrounded then
		-- The wished speed is capped and not the velocity, so steering in
		-- the air gains no speed.
		return accelerate(velocity, wish, math.min(speed, tuning.AirSpeedCap), tuning.AirAcceleration, dt)
	end

	-- Along the slope, so a ramp does not slow the walk.
	if wish ~= Vector3.zero then
		local ground = state.GroundNormal
		wish -= ground * dot(wish, ground)
		local length = math.sqrt(dot(wish, wish))
		if length > 1e-4 then
			wish /= length
		end
	end

	local level = math.sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z)
	if level > 0 then
		-- Slowed as if at StopSpeed below it, or the player never stops.
		local drop = math.max(level, tuning.StopSpeed) * tuning.GroundFriction * dt
		local kept = math.max(0, level - drop) / level
		velocity = Vector3.new(velocity.X * kept, velocity.Y, velocity.Z * kept)
	end

	return accelerate(velocity, wish, speed, tuning.GroundAcceleration, dt)
end

-- A jump asked for just before landing is kept for a few ticks, and one
-- asked for just after a ledge still counts. Returns the velocity, and
-- whether the player jumped.
local function jump(
	state: CharacterState,
	command: CharacterCommand,
	tuning: CharacterTuning,
	velocity: Vector3
): (Vector3, boolean)
	local wantsJump = command.JumpPressed or state.JumpBufferTicks > 0
	local canJump = state.IsGrounded or state.AirTicks <= tuning.CoyoteTicks
	if wantsJump and canJump and state.GroundSuppressTicks == 0 then
		state:LeaveGround()
		state.JumpBufferTicks = 0
		state.AirTicks = 255
		state.GroundSuppressTicks = tuning.GroundSuppressTicks
		return Vector3.new(velocity.X, tuning.JumpVelocity, velocity.Z), true
	end

	if command.JumpPressed then
		state.JumpBufferTicks = tuning.JumpBufferTicks
	elseif state.JumpBufferTicks > 0 then
		state.JumpBufferTicks -= 1
	end

	return velocity, false
end

-- Sweeps, and slides along what stopped the sweep, a few times. Returns
-- where that ends, the velocity that is left, and whether a wall was hit.
local function slide(
	state: CharacterState,
	collision: CharacterCollision,
	tuning: CharacterTuning,
	position: Vector3,
	velocity: Vector3,
	left: Vector3
): (Vector3, Vector3, boolean)
	local skin = tuning.SkinWidth
	local hitWall = false
	collision:ClearPlanes()
	for _ = 1, tuning.MaxSlideIterations do
		if dot(left, left) < skin * skin then
			break
		end

		local went, normal, gap, _, part = collision:Sweep(position, left)
		position += left * went
		if went >= 1 then
			break
		end

		-- Said now: the rest of the slide can end the tick clear of the part.
		if part and gap <= skin * 2 then
			state:AddSlideContact(part)
		end
		if normal.Y < tuning.MinGroundNormalY then
			hitWall = true
		end

		collision:AddPlane(normal)
		left = collision:Clip(left * (1 - went))
		velocity = collision:Clip(velocity)
	end

	return position, velocity, hitWall
end

-- Up, along the walk as it was before the wall clipped it, and down again.
-- Returns where the step ends, with the ground there. Nil when it is no step.
local function stepUp(
	collision: CharacterCollision,
	tuning: CharacterTuning,
	from: Vector3,
	walk: Vector3
): (Vector3?, Vector3, Instance?)
	local distance = math.sqrt(dot(walk, walk))
	if distance < 1e-4 then
		return nil, Vector3.zero, nil
	end

	local skin = tuning.SkinWidth
	local minGroundY = tuning.MinGroundNormalY
	local up = Vector3.new(0, tuning.StepHeight, 0)
	local rose = collision:Sweep(from, up)
	local raised = from + up * rose

	-- One tick of walking lands on the step's edge, which is too steep to
	-- stand on. So the probe reaches a little further than the walk.
	local edge = tuning.Radius * (1 - math.sqrt(math.max(0, 1 - minGroundY * minGroundY))) + skin * 4
	local forward = walk * (math.max(distance, edge) / distance)
	local went = collision:Sweep(raised, forward)
	local over = raised + forward * went

	-- Down a little further than it rose, so the landing is found.
	local down = Vector3.new(0, -(tuning.StepHeight * rose + skin * 2), 0)
	local fell, normal, _, point, part = collision:Sweep(over, down)
	if fell >= 1 or normal.Y < minGroundY or went <= 1e-3 then
		return nil, Vector3.zero, nil
	end

	-- The point that was hit and not the capsule: its round bottom can rest
	-- on the edge of a riser that is too tall to climb.
	if point.Y > from.Y + tuning.StepHeight + skin then
		return nil, Vector3.zero, nil
	end

	return over + down * fell, normal, part
end

-- Out of whatever the capsule is in, a little each time.
local function pushOut(
	collision: CharacterCollision,
	tuning: CharacterTuning,
	position: Vector3,
	velocity: Vector3
): (Vector3, Vector3)
	for _ = 1, tuning.MaxDepenetrationIterations do
		if collision:Gather(position, 0) == 0 then
			break
		end

		local push = collision:Solve(Vector3.zero)
		local length = math.sqrt(dot(push, push))
		if length < 1e-5 then
			break
		end
		if length > tuning.MaxPushPerTick then
			push *= tuning.MaxPushPerTick / length
		end

		position += push
		velocity = collision:Clip(velocity)
	end

	return position, velocity
end

-- Stands the player on the ground under it, or takes it off the ground.
-- Returns where it stands and the velocity the ground leaves it.
local function land(
	state: CharacterState,
	collision: CharacterCollision,
	tuning: CharacterTuning,
	position: Vector3,
	velocity: Vector3,
	wasGrounded: boolean
): (Vector3, Vector3)
	-- The ground is the plane under the player that faces up the most.
	local minGroundY = tuning.MinGroundNormalY
	local stands = false
	local ground = Vector3.yAxis
	local groundPart: Instance? = nil
	for index = 1, collision:Gather(position, tuning.SkinWidth * 2) do
		local normal, _, part = collision:Plane(index)
		if normal.Y >= minGroundY and (not stands or normal.Y > ground.Y) then
			stands, ground, groundPart = true, normal, part
		end
	end

	-- Down to the ground when it fell away under a walk, and also when the
	-- walk rises: a ramp would throw the player off at every crest.
	if not stands and wasGrounded and state.GroundSuppressTicks == 0 then
		local down = Vector3.new(0, -tuning.GroundSnapDistance, 0)
		local went, normal, _, _, part = collision:Sweep(position, down)
		if went < 1 and normal.Y >= minGroundY then
			position += down * went
			stands, ground, groundPart = true, normal, part
		end
	end

	if not stands then
		state:LeaveGround()
		return position, velocity
	end

	-- What drives into the ground goes, and the rise a ramp left.
	state:StandOn(ground, groundPart)
	local into = dot(velocity, ground)
	if into < 0 or (into > 0 and velocity.Y > 0) then
		velocity -= ground * into
	end

	return position, velocity
end

return function(
	state: CharacterState,
	command: CharacterCommand,
	collision: CharacterCollision,
	tuning: CharacterTuning,
	dt: number
)
	local wasGrounded = state.IsGrounded
	local position = state.Position
	local velocity, jumped = jump(state, command, tuning, steer(state, command, tuning, dt))
	if not state.IsGrounded then
		velocity += tuning.Gravity * dt
		if velocity.Y < -tuning.MaxFallSpeed then
			velocity = Vector3.new(velocity.X, -tuning.MaxFallSpeed, velocity.Z)
		end
	end

	local left = velocity * dt
	local hitWall
	position, velocity, hitWall = slide(state, collision, tuning, position, velocity, left)
	if hitWall and wasGrounded and not jumped then
		local landed, normal, part = stepUp(collision, tuning, position, Vector3.new(left.X, 0, left.Z))
		if landed then
			state.SteppedUpBy = math.max(0, landed.Y - position.Y)
			position = landed
			state:StandOn(normal, part)
		end
	end

	position, velocity = pushOut(collision, tuning, position, velocity)
	if not jumped then
		position, velocity = land(state, collision, tuning, position, velocity, wasGrounded)
	end

	if state.GroundSuppressTicks > 0 then
		state.GroundSuppressTicks -= 1
	end
	if state.IsGrounded then
		state.AirTicks = 0
	elseif state.AirTicks < 255 then
		state.AirTicks += 1
	end

	state.Position = position
	state.Velocity = velocity
end
