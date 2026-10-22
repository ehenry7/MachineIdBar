# History Traffic Statistics

> **Route:** Right-click menu → "History traffic statistics" (`HistoryTrafficDlg` + `HistoryTrafficCalendarDlg` + `HistoryTrafficListDlg`)
> **Storage:** binary file `CHistoryTrafficFile` (path: exe dir by default; per-day records of in/out bytes)
> **Generated:** 2026-10-22

## Overview

Aggregates daily up/down traffic into day/week/month/quarter/year views with a calendar and a detail list. Used to audit monthly bandwidth usage.

## Surfaces

### 1. History list dialog (main)
- **View type** selector: Day / Week / Month / Quarter / Year (`HistoryTrafficViewType`).
- Column list: period label, upload total, download total, total.
- Rectangles/bars scaled to values; **log scale** option (`use_log_scale`).
- Sortable columns; row click → detail.
- Navigation: prev/next period; "today" jump.

### 2. Calendar dialog
- Month grid (or week grid depending on view); cells colored/valued by traffic.
- **Sunday-first** toggle (`sunday_first`, default on).
- Day click → day detail list.

### 3. Detail list dialog
- Per-sub-period (e.g., per day within selected month) rows with up/down/total.

## Fields
| Field | Notes |
|-------|-------|
| View type | `HistoryTrafficViewType`: HV_DAY, HV_WEEK, HV_MONTH, HV_QUARTER, HV_YEAR |
| Sunday first | bool, default true |
| Log scale | bool, default false |
| Up/Down/Total per period | bytes, unit-formatted |

## Interactions
- Period selection updates all three surfaces (shared data source).
- Data written incrementally by the monitoring thread (per-day accumulator flushed to file).
- Missing days show as 0 / blank.

## Business Rules
- History file corrupt → dialog shows empty data, file rebuilt on next write.
- Yearly view aggregates 12 months; quarterly = 3-month windows starting Jan/Apr/Jul/Oct.
