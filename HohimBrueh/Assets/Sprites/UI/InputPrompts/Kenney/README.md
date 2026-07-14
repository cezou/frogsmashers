# Kenney Input Prompts Pixel

Source tile sheet for the generated input-prompt atlas
(`Assets/Resources/InputPromptAtlas.png`).

- Pack: "Input Prompts Pixel" by Kenney — https://kenney.nl/assets/input-prompts-pixel
- License: CC0 1.0 Universal (public domain, see License.txt)
- Sheet: 34×24 tiles of 16×16 px with 1 px spacing
  (tile index = row * 34 + column)

Regenerate the atlas after editing the tile table:
Unity menu `FrogSmashers/Generate Input Prompt Atlas`, or batch:

```
Unity.exe -batchmode -nographics -projectPath <project> \
  -executeMethod FrogSmashers.Editor.InputPromptAtlasGenerator.Generate \
  -quit -logFile -
```
