[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/preview.gif)

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)](https://github.com/tetreum/brickcraft/raw/main/Preview/world.gif)

[![Logo](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)](https://github.com/tetreum/brickcraft/raw/main/Assets/Textures/Logo.png)

# Brickcraft

WIP. Combining Lego like bricks + Minecraft buidling style in Unity.

[![Preview](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)](https://github.com/tetreum/brickcraft/raw/main/Preview/1.png)

## Blog

[https://tetreum.github.io/brickcraft/](https://tetreum.github.io/brickcraft/)

## Controls

- WASD - Move character
- Space - Jump
- TAB - Switch between fast inventory slots
- I - Open inventory
- Left click - Remove blocks
- Right click (having a block selected in inventory) - Adds a block
- Mouse wheel (having a block selected in inventory) - Rotates block

## How can i help?

[https://tetreum.github.io/brickcraft/?/help](https://tetreum.github.io/brickcraft/?/help)


## How can i add a new model?

1. Brick models & their prefabs are stored in https://github.com/tetreum/brickcraft/tree/main/Assets/Models/Bricks
2. The icon is stored at https://github.com/tetreum/brickcraft/tree/main/Assets/Resources/Textures/Bricks
3. Prefab must be listed at Server -> prefabs scene object.
4. Model specs must be added at Server.cs#setupBrickModels() (https://github.com/tetreum/brickcraft/blob/main/Assets/Scripts/Server.cs#L146)
5. Items using it must be added at Server.cs#items var (https://github.com/tetreum/brickcraft/blob/main/Assets/Scripts/Server.cs#L12)
6. To generate it's icon, head to /Scenes/IconGenerator & simply hit Play. Items with missing icons will have their icon generated.

## How can i add a new brick material/texture?

1. They're stored in Assets/Materials/BrickColors/ (https://github.com/tetreum/brickcraft/tree/main/Assets/Materials/BrickColors)
2. List the new materials at Canvas (scene object) -> Game -> BrickMaterials var.

## Credits

- Tapping sound effect - https://freesound.org/people/rioforce/sounds/233654/
- Dig + remove block sound effect - https://freesound.org/people/Agaxly/sounds/213005/
- Brick models - https://www.mecabricks.com/
- Break texture - MooCwzRck - https://www.minecraftforum.net/forums/mapping-and-modding-java-edition/resource-packs/1223258-16x128x-1-4-5-compatible-okami-texture-pack?page=5
- World chunk system - Smjert - https://github.com/chraft/chunk-light-tester/
- Logo font/style - Sverdlychenko Studio - http://sverdlychenko.com/en/lego-font-design/