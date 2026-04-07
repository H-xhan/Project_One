# AGENTS.md

## Project identity
Project One ("원") is a multiplayer physics-based party action game built with Unity and Unity Netcode.

## Success priority
1. Intent fidelity first.
   - Implement the exact designer/developer intent.
   - When the request is ambiguous in a way that can change gameplay, networking, public API, serialized data, or file scope, do read-only analysis first and do not edit code yet.
2. Correctness second.
   - Minimize regressions, desyncs, duplicate events, runtime errors, and unintended API drift.
3. Speed third, but still mandatory.
   - Move fast with the smallest safe diff.
   - Do not trade away intent fidelity or correctness for speculative speed.

## Default work mode
Always follow this loop:
1. Read and localize the problem.
2. Produce an Intent Lock.
3. Make the smallest safe change.
4. Validate.
5. Report exactly what changed, what was checked, and what risk remains.

## Intent Lock (required before any edit)
Before editing, state briefly:
- Intended player-facing outcome
- Non-goals
- Exact file(s) you believe matter
- Main regression risks

If ambiguity is HIGH-RISK (changes gameplay behavior, authority, serialized fields, public API, file scope, or scene/prefab assumptions):
- Do read-only analysis only.
- Ask up to 3 focused questions OR provide the safest assumption set and stop before editing.

If ambiguity is LOW-RISK and local:
- State the assumption explicitly.
- Proceed with the smallest safe change.

## Scope and change rules
- Prefer one-file changes by default.
- If a fix truly requires more than one file, stop after analysis and explain why the second file is necessary.
- Preserve existing structure, namespaces, class names, public APIs, serialized fields, RPC signatures, event names, and ScriptableObject schemas unless explicitly asked.
- No unsolicited refactors.
- No unsolicited renames.
- No formatting-only diffs.
- No dependency changes unless explicitly asked.
- No architecture changes unless explicitly asked.
- Never modify `.unity`, `.prefab`, `.asset`, or `.meta` files unless explicitly requested.
- Keep diffs minimal and reviewable.

## Project architecture rules
- Follow the "Hub 1 + Modules N" structure.
- Canonical multiplayer flow is: Lobby -> Ready -> Countdown -> Playing, unless the task explicitly changes that flow.
- Default inputs:
  - Left click = attack
  - Right click = interact / pickup
- Item pipeline uses:
  - `ItemDataSO`
  - `WeaponItemDataSO`
  - `ItemDatabaseSO`
- Equipment synchronization responsibility belongs to `PlayerEquipment`.
- Combat resolution responsibility belongs to `PlayerCombat`.
- Do not move combat logic into `PlayerEquipment`.
- Do not move equipment sync responsibility into `PlayerCombat`.

## Netcode rules
- Be explicit about ownership and authority.
- Preserve the existing authoritative model unless the task explicitly changes it.
- Avoid client/server divergence.
- Avoid duplicate hit registration.
- Avoid duplicate damage application.
- Check host/client asymmetry, not only host behavior.
- Treat late-join synchronization risk as a real risk when touching synced state.

## PlayerEquipment rules
When editing or reviewing equipment code:
- Focus on equip/unequip state, current slot, current equipped item, and their synchronization.
- Preserve visual/equipped state consistency across host and clients.
- Preserve references used by animation, attachment points, and item visuals.
- Guard against null item data, missing attach points, and invalid slot indices.
- Do not introduce combat-side damage logic here.

## PlayerCombat rules
When editing or reviewing combat code:
- Focus on attack gating, attack windows, hit detection, hit filtering, damage application, cooldown/state transitions, and authority.
- Ensure one valid target is damaged only as intended for each attack window.
- Preserve the existing friendly-fire/team rules unless explicitly asked.
- Preserve animation-event timing and existing gameplay feel unless explicitly asked.
- Do not introduce equipment synchronization responsibilities here.

## Validation policy
After edits, run the strongest available validation in this order:
1. Compile/build command for the repo, if available.
2. Targeted test command or reproduction command, if available.
3. Existing lint/format/pre-commit checks, if the repo already uses them.
4. Manual diff risk review.

If an automated validation command is unavailable, say so explicitly.
Never claim a command or test was run if it was not.

## Mandatory post-edit self-review checklist
Check for:
- Intent drift from the request
- Public API drift
- Serialized field drift
- RPC signature drift
- Ownership/authority regressions
- Duplicate hit or duplicate damage paths
- Null reference risk on hot paths
- Unintended scene/prefab/meta changes
- Responsibility leakage between `PlayerEquipment` and `PlayerCombat`

## Review guidelines
Treat the following as P1-level findings:
- Behavior no longer matches the requested design intent
- Host/client desync risk
- Wrong ownership or RPC direction
- Duplicate hit / duplicate damage risk
- Equipment state desync
- Unintended change to public API or serialized fields
- Null reference risk in common gameplay paths
- Accidental `.unity`, `.prefab`, `.asset`, or `.meta` changes
- A multi-file behavior change hidden inside a task that was requested as one-file only

Ignore style-only nits unless they hide logic risk.

## Final response format
Return results in this order:
1. What changed
2. Why this is the minimal safe change
3. What validation ran
4. Remaining risks (max 3)
5. If blocked, the exact blocker and the next safest action
