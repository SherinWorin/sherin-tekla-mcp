# Isometric View Drawings Study

Date: 2026-10-23

## Overview

Study of 21 ISOMETRIC VIEW drawings in the model to understand the structure and requirements for creating an automated macro.

## Drawings Analyzed

### Pattern Observed
All ISOMETRIC VIEW drawings follow a consistent structure:

**Drawing Identification:**
- Title2: "ISOMETRIC VIEW" (20 drawings) or "ISOMETRIC VIEWS" (1 drawing - [71])
- Title3: "3D"
- Drawing type: GA (General Arrangement)
- Block identification in Title1 (e.g., "BP01", "BP01M", "BP02", etc.)

### Reference Drawing: [115] BP01-80001

**Structure:**
- **3 Isometric views** per drawing
- All views named "3d"
- Scale: 1:20
- Views labeled as:
  - ISOMETRIC VIEW 1 (bottom left)
  - ISOMETRIC VIEW 2 (top right) 
  - ISOMETRIC VIEW 3 (top left)

**View Details from [115]:**

1. **ISOMETRIC VIEW 1** (ModelView_1587)
   - Position: x=181.8, y=337.9
   - Frame: x=87, y=331.7
   - Size: 209.4 x 216.8 mm
   - Objects: 336 total (studs, tracks, gypsum boards, jambs, plates, beams)

2. **ISOMETRIC VIEW 2** (ModelView_7261)
   - Position: x=565.8, y=480.1
   - Frame: x=447.9, y=307
   - Size: 208.7 x 250.8 mm
   - Objects: 315 total

3. **ISOMETRIC VIEW 3** (ModelView_24540)
   - Position: x=383.4, y=179.7
   - Frame: x=285, y=38.8
   - Size: 209.4 x 195.3 mm
   - Objects: 228 total

### Annotations

**Weld marks only:**
- ISOMETRIC VIEW 1: 22 weld marks
- ISOMETRIC VIEW 2: 4 weld marks  
- ISOMETRIC VIEW 3: 18 weld marks

No part marks, dimension sets, or text annotations in the isometric views themselves.

### Object Types Present

The isometric views show the complete 3D construction with:

1. **Structural Elements:**
   - STUD (CC61-0.7-7-35, St-52, class 7)
   - TOP_TRACK (C63*40*0.7, St-52, class 5)
   - BOTTOM_TRACK (C63*40*0.7, St-52, class 5)
   - MIDDLE_TRACK (C63*40*0.7, St-52, class 5)
   - JAMB (CC61-0.7-7-35, St-52, class 7)
   - SILL (C63*40*0.7, St-52, class 5)
   - LINTEL (C63*40*0.7, St-52, class 5)

2. **Secondary Elements:**
   - GYPSUM_BOARD (PL12.50, Gypsum, class 1) - ContourPlate
   - SHEET (PL1*100, Aluminum, class 6)
   - FRAME (RHS40*20*3, S275, class 11)
   - CEILING BEAM (RHS40*20*3, S275, class 9)
   - PLATE (PL4*60, S275, class 99)

### All 21 Drawings

| Mark | Block | Name | Status |
|------|-------|------|--------|
| [115] | BP01 | BP01-80001 | Reference (modified 2026-10-24) |
| [116] | BP01M | BP01M-80001 | Active |
| [117] | BP02 | BP02-80001 | Active |
| [118] | BP02M | BP02M-80001 | Active |
| [119] | BP03 | BP03-80001 | Active |
| [120] | BP04 | BP04-80001 | Active |
| [121] | BP04M | BP04M-80001 | Active |
| [122] | BP05 | BP05-80001 | Active |
| [123] | BP05M | BP05M-80001 | Active |
| [124] | BP06 | BP06-80001 | Active |
| [125] | BP06M | BP06M-80001 | Active |
| [126] | BP07 | BP07-80001 | Active |
| [127] | BP07M | BP07M-80001 | Active |
| [128] | BP08 | BP08-80001 | Active |
| [129] | BP08M | BP08M-80001 | Active |
| [130] | BP09 | BP09-80001 | Active |
| [131] | BP09M | BP09M-80001 | Active |
| [132] | BP10 | BP10-80001 | Active |
| [133] | BP10M | BP10M-80001 | Active |
| [71] | BP-01M | P016-MBCC-SD-BP-01M-DR-ST-801 | Older format |
| [74] | BP01 | BP01-80001 | Duplicate/older |

## Key Findings

### 1. Standard 3-View Layout
Every drawing has exactly **3 isometric views** arranged in a specific pattern on the A3 sheet (841 x 594 mm).

### 2. View Naming Convention
- All views named "3d"
- Distinguished by labels: "ISOMETRIC VIEW 1", "ISOMETRIC VIEW 2", "ISOMETRIC VIEW 3"
- Scale consistently 1:20

### 3. View Positions (Paper Space)
The three views are positioned at specific coordinates:
- **View 1**: Lower left quadrant
- **View 2**: Upper right quadrant  
- **View 3**: Upper left quadrant

### 4. Content
- Each view shows the complete 3D construction of wall panels
- Primary focus: Light gauge steel framing (studs, tracks)
- Secondary: Gypsum boards, sheets, frames
- Annotations: Weld marks only (no part marks or dimensions)

### 5. Color Coding (To Be Determined)
**REQUIRES MANUAL INSPECTION:**
- Need to examine the actual drawings in Tekla to determine:
  - Which elements use which colors
  - Color scheme for different material types
  - Color scheme for different element classes
  - Any special highlighting or emphasis

## Open Questions for Macro Development

### Critical (Must Answer)

1. **View Orientations:**
   - What are the exact 3D viewing directions for each view?
   - Are they standard isometric angles (e.g., 30°, 45°)?
   - Do they rotate around specific axes?

2. **Color Coding Scheme:**
   - What colors are applied to which element types?
   - Is color based on:
     - Material (St-52, S275, Gypsum, Aluminum)?
     - Class (1, 4, 5, 6, 7, 9, 11, 99)?
     - Part name (STUD, TRACK, GYPSUM_BOARD, etc.)?
     - A combination?

3. **Element Selection:**
   - Do all elements of the block appear in the views?
   - Are any elements filtered out?
   - Are gypsum boards always included?

4. **View Creation Method:**
   - Are views created programmatically via `View.Create3dView()`?
   - Or are they manually positioned and the macro just populates them?

### Important (Should Answer)

5. **Weld Mark Placement:**
   - Are weld marks placed automatically or manually?
   - What determines which welds get marked?

6. **View Frame Sizes:**
   - Are frame sizes fixed or calculated from content?
   - Current sizes vary slightly (195-250 mm range)

7. **Assembly Scope:**
   - Does one drawing show one block's assemblies only?
   - Or multiple blocks?

8. **Reference Drawing:**
   - Is [115] the canonical reference or just one example?
   - Should [71] (older naming) be considered?

### Nice to Have

9. **Drawing Properties:**
   - Should the macro set user properties (DR_DRAWN_BY, etc.)?
   - Should it update modification dates?

10. **Compatibility:**
    - Should it work with both naming conventions (BP01 vs BP-01M)?

## Proposed Macro Approach

### Phase 1: View Direction Discovery
1. Open [115] in Tekla
2. Inspect each of the 3 ModelViews
3. Extract the viewing direction vectors
4. Document the rotation angles

### Phase 2: Color Scheme Analysis
1. Manually inspect [115] in Tekla
2. Document the color of each element type
3. Determine the color assignment logic
4. Create a color mapping table

### Phase 3: Macro Design
Based on wall-framing macro pattern:
- Target: GA drawings with Title2 = "ISOMETRIC VIEW"
- Input: Block identifier (from Title1)
- Output: 3 isometric views with correct orientations and colors
- Safety: Refuse frozen/issued drawings

### Phase 4: Implementation
```
WF_IsometricRules.cs  - Rules for view creation (no Tekla dependency)
WF_Isometric.cs       - View creation and color application
P142_WF_Isometric.cs  - The loader
```

## Next Steps

**IMMEDIATE:**
1. **Manually inspect [115] in Tekla** to determine:
   - Actual colors applied to elements
   - Viewing directions of the 3 views
   - Any display settings (hidden lines, rendering mode)

2. **Check other drawings** ([116], [117]) to verify consistency

3. **Document the color scheme** as a mapping table

**THEN:**
4. Design the macro architecture
5. Write the rules (testable without Tekla)
6. Implement the view creation
7. Test on [74] (appears to be a test drawing)

## Reference

From the existing study: `macros/wall-framing/docs/SECTION-ELEVATION-AND-ISOMETRIC.md`

The isometric section (§8) notes:
- Reference: issued drawing of the client
- 3 ModelViews named "3d"
- **211 marks in the middle view** - but current data shows only weld marks
- Question 2: "Which three directions are the isometric views of?"
- Question 5: Create views or fill existing ones?
- Question 6: 211 marks - every part or only a family?

## API Members Confirmed

From Tekla 2025 API:
- `View.Create3dView(viewCoordinateSystem, viewScale, viewRotationAngle)` - creates 3D view
- `View.GetAllObjects()` - gets model objects in view
- View attributes available via `View.Attributes`

---

**Status:** Study complete, awaiting manual color inspection and view direction analysis.
