---
name: cad-bim-workflows
description: Support CAD, BIM and engineering-model workflows with geometry validation and reproducible exchange.
triggers: AutoCAD, DXF, DWG, Revit, BIM, CAD model, geometry import, IFC, ETABS model, structural model
---
# CAD and BIM workflows

- Identify source format, units, coordinate system, model version and intended downstream application.
- Preserve original files and create a new export or working copy.
- Validate geometry extents, layer/object types, duplicate entities, connectivity and units after conversion.
- For structural models, distinguish analytical centerlines from physical geometry and verify connectivity, releases, offsets and load paths.
- Never assume a CAD drawing contains all design intent; flag missing dimensions, levels and constraints.
- Do not claim to edit native CAD/BIM files unless a compatible tool is registered and the output is reopened for verification.
