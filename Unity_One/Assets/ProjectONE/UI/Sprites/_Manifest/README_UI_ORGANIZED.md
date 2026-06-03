# Project ONE UI Sprites Organized

## Folder Structure
- `Buttons`: Button sprites such as start, retry, home, back, and ready buttons.
- `Panels`: Panels, cards, frames, labels, banners, notes, boxes, and containers.
- `HUD`: In-game HUD sprites such as stamina, timer, mission, reward, status, progress, gauges, and bars.
- `MainMenu`: Main menu, quick start, custom game, tutorial, quit, and menu panel sprites.
- `Logo_Title`: Logo, title, project, subtitle, and slogan sprites.
- `Lobby_CharacterSelect`: Lobby, room code, ready, character select, select, and help sprites.
- `Result_Ranking`: Result, victory, ranking, medal, score, stamp, success, crown tab, and participant sprites.
- `Icons`: Icon sprites such as coin, clock, settings, sound, notice, plus, book, gamepad, power, and heart.
- `Decorations`: Tape, clip, pin, star, ribbon, sticker, folded corner, tag decoration, and decorative sprites.
- `Characters`: Hamster, mascot, avatar, character, face, and profile sprites.
- `ColorChips`: Color chip, swatch, palette, and named color reference sprites.
- `Needs_Check`: Sprites that did not clearly match a category keyword and require manual review.
- `_Duplicates/Exact`: Exact byte-for-byte SHA256 duplicates. These files are kept, not deleted.
- `_Duplicates/Near_Candidates`: Reserved folder for manually reviewing near duplicate candidates. Near duplicates are reported only and are not moved automatically.
- `_Manifest`: CSV reports and this README.

## Unity Import Settings
When `Apply Recommended Import Settings` is enabled, organized PNGs are imported as Sprite (2D and UI), Sprite Mode Single, Alpha Is Transparency enabled, Full Rect mesh, Bilinear filter, Clamp wrap, no mip maps, uncompressed texture compression, Sprite Pixels Per Unit 100, and Max Size 4096. `ColorChips` may use Max Size 512.

## Duplicates
Exact duplicates are detected by comparing PNG file bytes with SHA256. The selected keeper remains in its recommended category, and the other identical files are moved to `_Duplicates/Exact` using `AssetDatabase.MoveAsset` so Unity `.meta` GUID references stay intact.

## Needs Check
`Needs_Check` is intentionally conservative. Files in this folder were not strongly classified by the configured filename keywords and should be reviewed by a person before being used or renamed.

## 9-Slice Reminder
Buttons and panels may still need manual Sprite Editor setup. If a sprite should scale as UI chrome, configure its 9-slice Border in Unity's Sprite Editor after organization.
