# Project One Codex Review Prompts

## 1) Local CLI review (custom review instructions)
Use profile: `one_review`

Paste this into Codex custom review instructions:

Focus on Project One gameplay-intent regressions and Netcode risks.
Review for:
- intent drift from the requested feature or bugfix
- ownership / authority mistakes
- wrong ServerRpc / ClientRpc direction or missing guards
- duplicate hit registration or duplicate damage
- PlayerEquipment and PlayerCombat responsibility leakage
- null references in common gameplay paths
- unintended public API / serialized field drift
- accidental `.unity`, `.prefab`, `.asset`, or `.meta` changes
- missing validation for risky behavior changes

Severity rules:
- Treat gameplay regression, host/client desync, duplicate damage, broken equipment sync, and accidental API or serialized field changes as P1.
- Ignore style-only nits unless they hide logic risk.

## 2) Quick local slash flow
1. Start Codex with `one_review`
2. Run `/review`
3. Choose the diff scope you want
4. Use custom review instructions and paste the block above

## 3) GitHub PR review comment
Paste this in a PR comment:

@codex review for Project One intent drift, Unity Netcode ownership/authority regressions, wrong RPC direction, duplicate hit or damage paths, PlayerEquipment/PlayerCombat boundary violations, null refs in gameplay paths, accidental serialized field or API drift, and accidental scene/prefab/meta changes. Treat any gameplay regression or desync risk as P1.

## 4) GitHub fix-followup comment
If review found a real issue, use:

@codex fix the flagged issue with the smallest safe diff. Preserve public API, serialized fields, RPC signatures, and file responsibilities. Do not refactor. Re-run the strongest available validation and summarize remaining risk.
