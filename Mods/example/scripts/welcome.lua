function onLoad()
  log("Example mod loaded")
end

-- Texts come from the mod's lang/[language].json, in each player's language (English if it has no translation).
function onPlayerJoined(player)
  ui.title(player, text(player, "welcome.title"), text(player, "welcome.subtitle"), 4)

  local yes = text(player, "welcome.yes")
  ui.popup(player, {
    title = text(player, "welcome.popup_title", player.name),
    text = text(player, "welcome.popup_text"),
    buttons = {yes, text(player, "welcome.no")},
  }, function(button)
    if button == yes then
      if player:give("example:launcher", 1) then
        ui.toast(player, text(player, "welcome.given"))
      else
        ui.toast(player, text(player, "welcome.full"))
      end
    end
  end)
end

-- "!where" tells you where you are, "!shout" asks what to tell everyone; neither is sent to the chat
function onChat(player, text)
  if text == "!where" then
    local p = player.position
    player:message(text(player, "where", p.x, p.y, p.z))
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

-- Every hour of the game: at 20:00, players hear the night is coming.
function onHour(hour, day)
  if hour == 20 then
    for _, player in ipairs(world.players()) do
      player:message(text(player, "night", day))
    end
  end
end
