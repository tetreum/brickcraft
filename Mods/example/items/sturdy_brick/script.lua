-- Takes three hits to break. Each brick counts its own hits, by where it is.
local HITS = 3
local hits = {}

local function key(brick)
  local p = brick.position
  return p.x .. "," .. p.y .. "," .. p.z
end

function onHit(brick, player)
  local k = key(brick)
  hits[k] = (hits[k] or 0) + 1
  if hits[k] < HITS then
    player:message("It's sturdy (" .. hits[k] .. "/" .. HITS .. ")")
    return false
  end
end

function onBroken(brick, player)
  hits[key(brick)] = nil
end

function onPlaced(brick, player)
  hits[key(brick)] = nil
end
