# main menu

|      |           |          |         |
| ---- | --------- | -------- | ------- |
| Home | Customers | Admin    | Account |
|      |           | Users    | Profile |
|      |           | Machines | Status  |
|      |           | Settings | Logout  |

# Home page

combined journal/screenshot display

grid view showing a journal/image timestamp on the left, followed by each screen shot for that timestamp. Similar to the client "OfflineQueueWindow.axaml".

| Timestamp | Window | Desktop | Monitor 0 | Monitor 1 | Monitor 2 | Monitor 3 | Journal | Tags | Notes |
| --------- | ------ | ------- | --------- |---------- | --------- | --------- | ------- | --------- | ------- |
| 2026-07-22 12:34:56 | `Image` | `Image`   | `Image` | `Image` | `Image` |  `Image` | Customer/Project | admin | did some admin |
| 2026-07-22 12:44:56 | `Image` | `Image`   | `Image` | `Image` | `Image` |  `Image` | Customer/Project | admin | did more admin |

Click an individual image to popup a modal viewer window with zoom, pan, edit journal entry, edit tags, edit notes.

# Customers page

editable data grid

| Code | Name | Tags | Notes | Id (Admin only) | Edit | Save | Delete |
| ---- | ---- | ---- | ----- | --------------- | ---- | ---- | ------ |
| AERP | Advanced ERP | Us | | 06gqh59791t9vcrpcxwyb47txv | `edit` | `save` | `delete`|
| MI   | McCallum Industries | Syspro | | 06gruxt6x5uzme4xrpxecwwxep | `edit` | `save` | `delete` |

`edit` button opens dedicated edit page.

## customer editor

also accessible by url .../customer/customer-code

editable form

| | |
| - | - |
| Code: [`      `]  |     Notes: [`            `] |
| Name: [`      `]  |     |
| Tags: [`      `]  |     |

editable projects grid
note: project 'Code' is unique within customer.

| Code | Name | Tags | Notes | Id (Admin only) | Edit | Save | Delete |
| ---- | ---- | ---- | ----- | --------------- | ---- | ---- | ------ |
| Schnapp  | Schnapp Development | dev | | u7q4n0gwx105tbqs0dzq8s4rg8 | `edit` | `save` | `delete` |

`edit` button opens dedicated edit page.

editable tasks grid
note: Task 'Code' is unique within customer. Project tasks get their own per-project grid.

| Code | Name | Tags | Notes | Id (Admin only) | Edit | Save | Delete |
| ---- | ---- | ---- | ----- | --------------- | ---- | ---- | ------ |
| Admin | Administration | admin | | id26-id | `edit` | `save` | `delete`|
| Invoicing | Invoicing | admin xero | | id26-id | `edit` | `save` | `delete` |

## project editor

editable form

| | |
| - | - |
|Code: [`Schnapp`]              |  Notes: [`            `] |
|Name: [`Schnapp Development`]  |   |
|Tags: [`      `]               |   |

editable tasks grid
note: Task 'Code' is unique within project, inherits Project's customer_id.

| Code | Name | Tags | Notes | Id (Admin only) | Edit | Save | Delete |
| ---- | ---- | ---- | ----- | --------------- | ---- | ---- | ------ |
| Scope | Planning | plan | | id26-id | `edit` | `save` | `delete`|
| Dev  | Schnapp Development | dev | | id26-id | `edit` | `save` | `delete` |
| Test  | Schnapp Testing | dev | | id26-id | `edit` | `save` | `delete` |
| Deploy | Schnapp Deployment | dev | | id26-id | `edit` | `save` | `delete` |

`edit` button opens dedicated edit page.

## task editor

editable form
| | |
| - | - |
| Code: [`      `]  |     Notes: [`            `] |
| Name: [`      `]  |     |
| Tags: [`      `]  |     |   

# Admin Menu

## Users Page

editable data grid

| Code | Name | Email | Role | Time zone | Id (Admin only) | Edit | Password | Save | Delete |
| ---- | ---- | ----- | ---- | --------- | --------------- | ---- | -------- | - | - |
| Ryan | Ryan | ryan@aerp.co.nz | admin | Pacific/Auckland | id26-id | `edit` | `set password` | `save` | `delete`|
| Trev | Trevor | trevor@aerp.co.nz | user | Pacific/Auckland | id26-id | `edit` | `set password` | `save` | `delete` |

`edit` opens a per-user edit page.
`password` should use a simple new password popup with a show/hide password toggle.

## Machines Page

editable grid... same pattern

## Settings Page

editable key/value/notes grid.

# Account menu

## Profile

- self-service edit form
| | |
| - | - |
| Code: | [`      `] (readonly) |
| Name: | [`                   `] |
| Email: | [`                   `] |
| Timezone: | [`                   v`] |

- read-only lists of associated machines.

| Name | Code | FQDN | Id |
| ---- | --------- | -------- | ------- |
| ryan-dtk | ryan-dtk-b0599f | ryan-dtk.price.org.nz | `06gqh53jfxybg8fqxpjr74w0rv` |

## Status

Current is fine, but remove token.



