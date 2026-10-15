# Roadmap

Follows the project phases: build -> test -> validate -> document, one step at a time.

## Next (v0.2)
- [ ] Live test on Tekla 2025 (docs/TESTING.md section 4) and fill in API_NOTES "Live 2025".
- [ ] Confirm `boundary-wall` rule values (see note below) and the minimum cut panel length.
- [ ] `tekla_create_wall_from_plan`: turn a `tekla_plan_wall` result into columns + panels (preview/confirm like other writes).
- [ ] `tekla_check_gaps`: measure gaps between selected panels against a rule.
- [ ] Excel (.xlsx) export in addition to CSV.

## Later
- [ ] Contour plates (`tekla_create_plates`).
- [ ] Bulk report properties with the 2025 multi-object `GetReportProperty`.
- [ ] Reconnect after a Tekla restart without restarting the client.
- [ ] MCP tool annotations (readOnlyHint / destructiveHint) once confirmed in the SDK.
- [ ] Base-skid and loading-platform rule sets.
- [ ] Drawing tools (read-only first).

## Note on the boundary-wall rule
With the brief's values (400 mm column along the wall, 3500-3600 mm c/c, 3190 mm panel, 10 mm gaps) one standard panel needs 3190 + 2 x 10 = 3210 mm, but a 3600 bay leaves only 3600 - 400 = 3200 mm. So either the column is set with its 200 mm side along the wall, the spacing is 3610 mm, or the gap/panel values differ. The rule file keeps the brief's values; `tekla_plan_wall` reports the result honestly (a 3180 mm cut panel per bay). Fix `config/rules/boundary-wall.json` once confirmed.
