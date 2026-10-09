-- Use it (E) and it shoots a plate of a random colour up into the air.
local COLORS = {1, 4, 14, 10, 22, 191}

function onInteract(brick, player)
  local p = brick.position
  local plate = world.spawn("plate_1x1_green", {x = p.x, y = p.y + 3, z = p.z}, {color = COLORS[math.random(#COLORS)]})
  if not plate then
    player:message("Something is on top of the launcher")
    return
  end

  local id
  id = timer.every(0.05, function()
    local q = plate.position
    if q.y > p.y + 40 or not plate:move({x = q.x, y = q.y + 1, z = q.z}) then
      plate:remove()
      timer.cancel(id)
    end
  end)
end
