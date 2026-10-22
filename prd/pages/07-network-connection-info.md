# Network / Connection Info

> **Route:** Right-click menu → "Connection details" (`NetworkInfoDlg`)
> **Generated:** 2026-10-22

## Overview

Read-only detail view of the currently selected network connection: IP configuration (IPv4/IPv6, gateway, DHCP status), totals, and live counters. Refreshed live while open.

## Layout
Table/list of labeled rows grouped by:
- Connection identity (name, description, type)
- IPv4: address, subnet mask, gateway, DHCP enabled/obtained, DHCP server
- IPv6: address(es), gateway, prefix
- Counters: total received bytes, total sent bytes (since boot, from `GetIfTable` counters)
- Optional: external (internet) IP [TBC — gated by legacy `m_show_internet_ip` setting, commented out in current code]

## Fields
| Field | Source |
|-------|--------|
| Connection name / description | `MIB_IFTABLE` (`bIfDesc`) |
| Index / type | `dwIndex`, `type` (maps to friendly name) |
| IPv4 addresses | `GetAdaptersAddresses` / `GetIfTable` + `GetIpAddrTable` [TBC exact helper — `NetworkInfoDlg.cpp`] |
| Gateway / mask / DHCP | adapter IP API family |
| In/Out bytes | `inOctets`/`outOctets` |

## Interactions
- Live refresh via main monitoring cycle (dialog re-queries on each timer tick).
- Close button only; no editing.

## Business Rules
- Only the *selected* connection is shown (all-adapters mode shows the aggregated row).
