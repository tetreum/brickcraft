function onLoad()
  log("Example mod loaded")
end

function onPlayerJoined(player)
  player:message("Welcome, " .. player.name .. "! This world has the Example mod: craft a Launcher and use it with E.")
end

-- "!where" in the chat tells where you are, and isn't sent to the others
function onChat(player, text)
  if text == "!where" then
    local p = player.position
    player:message("You're at " .. p.x .. ", " .. p.y .. ", " .. p.z)
    return false
  end
end
