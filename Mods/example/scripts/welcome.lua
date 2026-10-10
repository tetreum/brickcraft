function onLoad()
  log("Example mod loaded")
end

function onPlayerJoined(player)
  ui.title(player, "Example mod", "Bricks with scripts", 4)

  ui.popup(player, {
    title = "Welcome, " .. player.name .. "!",
    text = "This world has the Example mod. The Launcher shoots plates up into the air when you use it (E). Want one?",
    buttons = {"Yes please", "No thanks"},
  }, function(button)
    if button == "Yes please" then
      if player:give("example:launcher", 1) then
        ui.toast(player, "There's a Launcher in your inventory")
      else
        ui.toast(player, "Your inventory is full")
      end
    end
  end)
end

-- "!where" tells you where you are, "!shout" asks what to tell everyone; neither is sent to the chat
function onChat(player, text)
  if text == "!where" then
    local p = player.position
    player:message("You're at " .. p.x .. ", " .. p.y .. ", " .. p.z)
    return false
  end

  if text == "!shout" then
    ui.popup(player, {
      title = "Shout",
      text = "Everyone will see it in the middle of their screen.",
      input = {placeholder = "What do you want to shout?"},
      buttons = {"Shout", "Cancel"},
    }, function(button, message)
      if button == "Shout" and message ~= "" then
        for _, other in ipairs(world.players()) do
          ui.title(other, message, player.name .. " shouts", 3)
        end
      end
    end)
    return false
  end
end

function onPlayerLeft(player)
  shared.launches = shared.launches or {}
  shared.launches[player.id] = nil
end
