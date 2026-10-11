-- The moss golem rumbles at players who use it while it's wild (using it with leaves in hand tames it).
function onInteract(npc, player)
  if npc.owner == nil then
    player:message("The moss golem rumbles. It seems to like leaves.")
  end
end
