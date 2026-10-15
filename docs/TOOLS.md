# Tools

Units: millimetres and kilograms. Points: `"x,y,z"` in **global** model coordinates. GUID lists: separated by commas, spaces or new lines.

## LOW risk - read, calculate, UI only

| Tool | Parameters | Returns |
|---|---|---|
| `tekla_get_connection_info` | - | connected, model name/path, Tekla bin folder + Model.dll version, ReadOnly / AllowDelete, settings file, log folder |
| `tekla_get_selected_objects` | `includeUdas` (false), `limit` (200) | objects: guid, id, type, isPart, name, profile, material, class, finish, part/assembly prefix + start number, PART_POS, ASSEMBLY_POS, start/end (or contour), axisLengthMm, reportLengthMm (LENGTH), weightKg (WEIGHT), position plane/depth/rotation, isVertical, udas |
| `tekla_get_objects` | **`guids`**, `includeUdas` | same, plus `notFound` |
| `tekla_find_objects` | `types`, `profile`, `profileContains`, `material`, `teklaClass`, `nameContains`, `orientation` (vertical / nonvertical), `includeUdas`, `limit` | matching parts, `truncated`, `scanned` |
| `tekla_select_objects` | **`guids`** | selects them in Tekla |
| `tekla_highlight_objects` | **`guids`**, `red`, `green`, `blue` (0..1) | temporary colour in the view |
| `tekla_quantity_report` | `source` (selected / guids / filter), `guids`, filter fields, `groupBy` (profile, material, name, class), `exportCsv` | rows: quantity, running length (m), weight (kg); totals; CSV path |
| `tekla_validate_objects` | `source`, `guids`, filter fields, `panelRule` | issues (missing profile/material/class/name, short members, duplicates, panel length vs rule), `problemGuids` |
| `tekla_calculate_distance` | **`pointA`**, **`pointB`** | distance, dx/dy/dz, horizontal distance, angle to horizontal |
| `tekla_list_rules` | - | all rules in `config/rules` |
| `tekla_plan_wall` | **`rule`**, `wallLengthMm` or `startPoint` + `endPoint` | columns, bays, spacing (checked), column centres, panels per bay |
| `tekla_divide_opening` | **`rule`**, **`clearLengthMm`** | standard panels, cut panel, gaps, unfilled length, offsets |

## MEDIUM / HIGH risk - change the model (two steps)

All four take `intent` (the user's request, logged), `apply` (false = preview) and `confirmToken` (from the preview).

| Tool | Parameters |
|---|---|
| `tekla_create_beams` | **`beams`**: list of `{ start, end, profile, material, class?, name?, positionPlane?, positionDepth?, positionRotation? }` |
| `tekla_create_columns` | **`columns`**: list of `{ base, heightMm, profile, material, class?, name?, position...? }` |
| `tekla_modify_objects` | **`guids`**, `profile`, `material`, `teklaClass`, `name`, `finish`, `udas` (`"NAME=value; NAME2=value"`) |
| `tekla_delete_objects` | **`guids`** - only when `AllowDelete` is true; always HIGH risk |

### Write result

```json
{
  "status": "preview | applied | partial | refused | failed",
  "operation": "modify_objects",
  "operationId": "3f2a9c...",
  "risk": "Medium",
  "summary": "Will change 6 field(s) on 6 object(s). PREVIEW ONLY - nothing has been changed.",
  "plan": ["Modify 6 object(s): material=S355JR."],
  "changes": [{ "guid": "...", "field": "Material", "before": "S275JR", "after": "S355JR" }],
  "confirmToken": "....",
  "confirmTokenExpiresUtc": "2026-10-15T19:10:00Z",
  "nextStep": "Show this plan to the user ... call again with apply=true and confirmToken.",
  "affectedGuids": [], "created": [], "validation": [], "warnings": [], "errors": []
}
```

Risk rules: create/modify up to `HighRiskObjectCount` objects = MEDIUM, more = HIGH, delete = HIGH. Refused when `ReadOnly` is true, when delete is not allowed, above `MaxObjectsPerWrite`, for unknown GUIDs, unknown profiles/materials, invalid points, members shorter than `MinMemberLengthMm`, or a missing/expired/mismatched token.
