-- The moss golem rumbles at players who use it while it's wild (using it with leaves in hand tames it).
function onInteract(npc, player)
  if npc.owner == nil then
    player:message("The moss golem rumbles. It seems to like leaves.")
  end
end

-- Its schedule (info.json) sends it to bed at 21:00, in a bed near where it appeared if there is one.
function onActivity(npc, activity)
  if activity == "sleep" then
    log(npc.name .. " goes to sleep, it's " .. string.format("%02d:%02d", math.floor(world.time()), math.floor(world.time() % 1 * 60)))
  end
end
