# Project One Codex Work Templates

## A. PlayerEquipment one-file work template
Use profile: `one_impl`

```text
[Project One | PlayerEquipment | one-file implement]

First do an Intent Lock:
1) intended gameplay result
2) non-goals
3) why PlayerEquipment.cs is the correct file
4) main networking / regression risks

Then modify only PlayerEquipment.cs to solve this request:
[요청 / 증상 / 목표]

Constraints:
- Other file edits are forbidden.
- Preserve class name, namespace, public API, serialized fields, RPC signatures, and event names.
- No refactor, rename, formatting-only cleanup, or dependency changes.
- Keep PlayerEquipment responsible only for equipment state and synchronization.
- Do not move combat logic here.
- If the fix truly requires another file, stop after analysis and explain the exact second file needed and why.

Must verify:
- owner / server authority is still correct
- equip / unequip state is deterministic on host and clients
- current slot / current equipped item does not desync or duplicate
- no null refs from item data, attach point, animator, or references
- no accidental public API or serialized field drift
- no accidental `.unity`, `.prefab`, `.asset`, or `.meta` changes

Validation:
- Run: [BUILD_COMMAND]
- Run: [TARGETED_REPRO_OR_TEST_COMMAND]
- If unavailable, state unavailable and do a manual diff risk review

Final output format:
- changed lines summary
- why this is the minimal safe change
- validation results
- remaining risks (max 3)
```

## B. PlayerCombat one-file work template
Use profile: `one_impl`

```text
[Project One | PlayerCombat | one-file implement]

First do an Intent Lock:
1) intended gameplay result
2) non-goals
3) why PlayerCombat.cs is the correct file
4) main combat / networking / regression risks

Then modify only PlayerCombat.cs to solve this request:
[요청 / 증상 / 목표]

Constraints:
- Other file edits are forbidden.
- Preserve class name, namespace, public API, serialized fields, RPC signatures, animation-event names, and layer/filter assumptions unless explicitly asked.
- No refactor, rename, formatting-only cleanup, or dependency changes.
- Keep PlayerCombat responsible for attack validation, hit detection, damage application, cooldown/state transitions, and combat authority.
- Do not move equipment synchronization here.
- If the fix truly requires another file, stop after analysis and explain the exact second file needed and why.

Must verify:
- attack gate / cooldown / state transitions still behave correctly
- one target is hit only as intended for each attack window
- no duplicate hit from host/client asymmetry or multi-collider overlap
- damage authority still matches the existing model
- no unintended friendly-fire / team-rule change
- no null refs from weapon data, owner refs, hitboxes, animator, or target health path
- no accidental public API or serialized field drift
- no accidental `.unity`, `.prefab`, `.asset`, or `.meta` changes

Validation:
- Run: [BUILD_COMMAND]
- Run: [TARGETED_REPRO_OR_TEST_COMMAND]
- If unavailable, state unavailable and do a manual diff risk review

Final output format:
- changed lines summary
- why this is the minimal safe change
- validation results
- remaining risks (max 3)
```

## C. Read-only analysis template before touching code
Use profile: `one_plan`

```text
[Project One | read-only analysis]

Analyze this request without editing any files:
[요청 / 증상 / 목표]

Return:
1) intended gameplay result
2) exact file(s) that should own this change
3) whether this can stay one-file or not
4) authority / synchronization / regression risks
5) the smallest safe implementation plan

Do not modify code yet.
```
