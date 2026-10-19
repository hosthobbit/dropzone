# Attribution

## Original to this project

Everything below was made for DropZone by Host Hobbit, in code:

- **3D models.** The board with its 42 holes, the stand, the counters with their embossed star and ring symbols, the platform and the arena are built from primitive shapes by `Assets/DropZone/Editor/MeshKit.cs`, `DropZoneBuilder.cs` and `SceneBuilder.cs`.
- **Textures.** Brushed metal, the floor grid, wall panels, glows, rings and beams are drawn procedurally by `Assets/DropZone/Editor/TextureKit.cs`.
- **Icons.** The star, ring, Bomb and Swap icons are drawn at start-up by `Assets/DropZone/Scripts/UI/UiKit.cs`.
- **Sound effects and music.** These are synthesised at runtime by `Assets/DropZone/Scripts/Audio/Sfx.cs`. No recorded audio is used.
- **Game design and code.** This includes the Battle rules (Bomb and Swap), the computer opponent, the UI and the WebGL loading page.

No third-party art, models, sounds or code snippets were imported. DropZone is an original four-in-a-row game. It doesn't use any other game's name, branding or packaging.

## Fonts

Both fonts are used under the [SIL Open Font License 1.1](https://openfontlicense.org). Their licence texts sit next to the font files in `Assets/DropZone/Resources/Fonts/`.

| Font | Used for | Copyright | Source |
|---|---|---|---|
| Lilita One | titles and headings | © 2011 Juan Montoreano, Reserved Font Name "Lilita" | [google/fonts](https://github.com/google/fonts/tree/main/ofl/lilitaone) |
| Noto Sans Bold / Black | UI text | © 2022 The Noto Project Authors | [notofonts/latin-greek-cyrillic](https://github.com/notofonts/latin-greek-cyrillic) |

## Engine and packages

Made with Unity 6000.6.3f1 and Unity's own packages: Universal Render Pipeline, Input System, uGUI and Test Framework. These are used under the [Unity Terms of Service / Companion License](https://unity.com/legal). They are not included in this repository; Unity Package Manager downloads them.

---

Developed by [Host Hobbit](https://hosthobbit.com).
