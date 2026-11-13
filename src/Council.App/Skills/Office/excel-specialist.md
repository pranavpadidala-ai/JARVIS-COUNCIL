---
triggers: Excel, spreadsheet, workbook, worksheet, formula, pivot table, chart, interpolation, average data, data table, engineering calculations
name: excel-specialist
description: Use for Excel workbooks, spreadsheets, formulas, engineering calculations, data cleaning, charts, formatting, and workbook review.
---

# Excel specialist

## Before changing a workbook
1. List available files and inspect the target workbook. Confirm sheet names, used ranges, headers, formulas, units, merged cells, hidden rows/columns, and existing formatting.
2. Determine whether values are inputs, formulas, or cached formula results. Never replace formulas with hard-coded values.
3. Preserve macros in .xlsm files; never save macro-enabled workbooks through a tool that drops VBA.
4. Make a separate output copy by default. Never overwrite the original.
5. If a column, unit, axis, or load case is ambiguous, ask before calculating.

## Spreadsheet quality
- Use formulas for calculated cells so the workbook remains editable.
- Keep input assumptions separate from calculated outputs. Label units in headers and use consistent units.
- Use absolute/relative references intentionally; check formula ranges and copied formulas for off-by-one errors.
- Avoid unsupported modern formulas if the target calculation engine cannot evaluate them.
- For structural engineering, preserve significant figures and units; show equations, substitutions, and intermediate values where useful.
- For graphs, label both axes with quantity and units, use appropriate series names, and avoid smoothing or interpolation unless requested.
- Document assumptions and data sources in visible notes or cell comments.
- Check for #REF!, #VALUE!, #DIV/0!, #NAME?, and other formula errors. Verify sample cells independently.

## Data analysis and charts
- Clean data without silently dropping observations. Report rows removed and why.
- For averaging or interpolation, identify the method, align independent-variable points, and do not extrapolate unless requested.
- For experimental plots, preserve original series and add derived series separately.
- Inspect chart ranges, axis bounds, units, legends, and plotted series after creation.

## Current JARVIS tool contract
Use office_workspace action list_documents, inspect_document, or create_workbook only. create_workbook accepts a JSON array of row arrays. The current tool does not edit existing workbooks, insert formulas, or create charts yet. Do not invent unsupported actions.
