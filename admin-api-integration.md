# Admin Panel — Frontend Integration Reference (March 2026)

This document describes the **current state of every admin API endpoint** after the latest backend changes.
Use it as the single source of truth when building or updating the admin panel API client.

---

## 1. Common Conventions

### Base URL
```
/api/admin
```
All endpoints require the `Authorization: Bearer <token>` header obtained from the login endpoint.

### Standard Response Envelope
Every endpoint wraps its payload in:
```json
{
  "succeeded": true,
  "message": "Success",
  "data": { }
}
```
On error:
```json
{ "succeeded": false, "message": "Human-readable error", "data": null }
```

### Pagination Envelope
All list endpoints return:
```json
{
  "succeeded": true,
  "message": "Success",
  "data": {
    "items": [],
    "totalCount": 150,
    "page": 1,
    "pageSize": 20,
    "totalPages": 8,
    "hasNextPage": true,
    "hasPreviousPage": false
  }
}
```

---

## 2. ⚠️ REMOVED Endpoints — Delete From Your API Client

The following endpoints **no longer exist**. Remove every reference (API service, Zustand store slice, React Query key, UI component) that calls them.

| Method | Endpoint | Why removed |
|---|---|---|
| `PUT` | `/api/admin/societies/:id` | Societies are read-only from admin panel |
| `PUT` | `/api/admin/subscriptions/:id` | Subscriptions are read-only from admin panel |
| `GET` | `/api/admin/payments/:id` | Single-payment detail removed; use list only |
| `GET` | `/api/admin/features` | Feature Flags module deleted entirely |
| `GET` | `/api/admin/features/:id` | — same — |
| `POST` | `/api/admin/features` | — same — |
| `PUT` | `/api/admin/features/:id` | — same — |
| `DELETE` | `/api/admin/features/:id` | — same — |

---

## 3. Authentication

### `POST /api/admin/auth/login`
No auth required.

**Request body**
```json
{ "email": "admin@example.com", "password": "secret" }
```
**Response `data`**
```json
{
  "accessToken": "eyJ...",
  "accessTokenExpiresAt": "2026-03-24T10:00:00Z",
  "adminPublicId": "uuid",
  "name": "Super Admin",
  "email": "admin@example.com"
}
```

---

### `GET /api/admin/auth/me`
**Response `data`**
```json
{
  "publicId": "uuid",
  "name": "Super Admin",
  "email": "admin@example.com",
  "isActive": true,
  "lastLogin": "2026-03-22T08:00:00Z",
  "createdAt": "2025-01-01T00:00:00Z"
}
```

---

## 4. Plans

### `GET /api/admin/plans`
| Param | Type | Notes |
|---|---|---|
| `page` | int | Default 1 |
| `pageSize` | int | Default 20 |
| `search` | string? | Filter by name |
| `isActive` | bool? | |

**Response item**
```json
{
  "id": "uuid",
  "name": "Pro",
  "monthlyAmount": 999.00,
  "currency": "INR",
  "isActive": true,
  "durationMonths": 12,
  "createdAt": "2025-01-01T00:00:00Z"
}
```

### `GET /api/admin/plans/{id:guid}`

### `POST /api/admin/plans`
```json
{ "name": "Pro", "monthlyAmount": 999.00, "currency": "INR", "durationMonths": 12 }
```

### `PUT /api/admin/plans/{id:guid}`
> Plans are **never hard-deleted** — set `isActive: false` to deactivate.
```json
{ "name": "Pro", "monthlyAmount": 999.00, "currency": "INR", "isActive": false, "durationMonths": 12 }
```

### `DELETE /api/admin/plans/{id:guid}`
Hard delete — only use when no subscriptions exist for the plan.

---

## 5. Societies — UPDATED

### `GET /api/admin/societies`
| Param | Type | Notes |
|---|---|---|
| `page` | int | Default 1 |
| `pageSize` | int | Default 20 |
| `search` | string? | Filter by name |

**Response item** (aggregate fields are `0`/`null` in list mode)
```json
{
  "id": 1,
  "publicId": "uuid",
  "name": "Sunshine Apartments",
  "address": "123 Main St",
  "city": "Mumbai",
  "state": "Maharashtra",
  "pincode": "400001",
  "currency": "INR",
  "defaultMaintenanceCycle": "monthly",
  "createdAt": "2025-06-01T00:00:00Z",
  "updatedAt": "2026-01-10T00:00:00Z",
  "isDeleted": false,
  "deletedAt": null,
  "onboardingDate": "2025-06-01",
  "flatCount": 0,
  "activeFlatCount": 0,
  "userCount": 0,
  "activeUserCount": 0,
  "activeSubscription": null
}
```

---

### `GET /api/admin/societies/{id:long}` — UPDATED
Returns full society detail with **live aggregate counts** and **active subscription summary**.

**Additional fields in the detail response (not populated in the list)**
```json
{
  "flatCount": 48,
  "activeFlatCount": 45,
  "userCount": 3,
  "activeUserCount": 2,
  "activeSubscription": {
    "id": "uuid",
    "planName": "Pro",
    "status": "active",
    "subscribedAmount": 999.00,
    "currency": "INR",
    "currentPeriodEnd": "2026-06-01T00:00:00Z",
    "trialEnd": null
  }
}
```

> **UI suggestion:** Show a detail page/drawer with:
> - Info section (address, city, state, currency, maintenance cycle, joined date)
> - Stats card row: Total Flats | Active Flats | Total Users | Active Users
> - Subscription badge: plan name + status chip + period end date

---

## 6. Users — NEW

### `GET /api/admin/users`
| Param | Type | Notes |
|---|---|---|
| `page` | int | Default 1 |
| `pageSize` | int | Default 20 |
| `societyId` | long? | Filter by society |
| `search` | string? | Matches name, email, or mobile |
| `isActive` | bool? | |
| `isDeleted` | bool? | Omit to exclude deleted (default behaviour) |

**Response item**
```json
{
  "id": 101,
  "publicId": "uuid",
  "societyId": 1,
  "societyName": "Sunshine Apartments",
  "name": "Ravi Kumar",
  "email": "ravi@example.com",
  "mobile": "9876543210",
  "username": "ravi.kumar",
  "roleId": 1,
  "isActive": true,
  "isDeleted": false,
  "lastLogin": "2026-03-20T14:30:00Z",
  "createdAt": "2025-06-15T00:00:00Z",
  "subscriptionStatus": "active",
  "subscriptionPlan": "Pro",
  "trialEndsDate": null,
  "nextBillingDate": "2026-04-01T00:00:00Z"
}
```

### `GET /api/admin/users/{id:long}` — NEW
Returns a single user. Same shape as list item.

> **UI suggestion:** Table with columns: Name, Society, Role, Active badge, Subscription badge, Last Login. Click row → detail side-panel.

---

## 7. Subscriptions — READ-ONLY

No write endpoints. Remove any create/edit UI.

### `GET /api/admin/subscriptions`
| Param | Type | Notes |
|---|---|---|
| `page` | int | Default 1 |
| `pageSize` | int | Default 20 |
| `status` | string? | `trial` `active` `cancelled` `expired` `past_due` |
| `userId` | long? | |

**Response item**
```json
{
  "id": "uuid",
  "userId": 101,
  "userName": "Ravi Kumar",
  "userEmail": "ravi@example.com",
  "planId": "uuid",
  "planName": "Pro",
  "status": "active",
  "subscribedAmount": 999.00,
  "currency": "INR",
  "currentPeriodStart": "2026-03-01T00:00:00Z",
  "currentPeriodEnd": "2026-06-01T00:00:00Z",
  "trialStart": null,
  "trialEnd": null,
  "cancelledAt": null,
  "createdAt": "2025-06-15T00:00:00Z",
  "updatedAt": "2026-03-01T00:00:00Z"
}
```

### `GET /api/admin/subscriptions/{id:guid}`
Single subscription by GUID.

---

## 8. Payments — READ-ONLY, LIST ONLY

No write endpoints. No single-payment detail endpoint.

### `GET /api/admin/payments`
| Param | Type | Notes |
|---|---|---|
| `page` | int | Default 1 |
| `pageSize` | int | Default 20 |
| `societyId` | long? | |
| `paymentType` | string? | e.g. `maintenance`, `advance` |
| `from` | DateTime? | createdAt ≥ |
| `to` | DateTime? | createdAt ≤ |

**Response item**
```json
{
  "id": 5001,
  "publicId": "uuid",
  "societyId": 1,
  "billId": 200,
  "flatId": 10,
  "amount": 3500.00,
  "datePaid": "2026-03-15T00:00:00Z",
  "modeCode": "UPI",
  "reference": "UPI12345",
  "paymentType": "maintenance",
  "razorpayPaymentId": null,
  "verifiedAt": "2026-03-15T10:00:00Z",
  "isDeleted": false,
  "createdAt": "2026-03-15T09:55:00Z"
}
```

---

## 9. Bills — NEW

Cross-society bill monitoring. Read-only.

### `GET /api/admin/bills`
| Param | Type | Notes |
|---|---|---|
| `page` | int | Default 1 |
| `pageSize` | int | Default 20 |
| `societyId` | long? | |
| `status` | string? | `unpaid` `paid` `partial` |
| `period` | string? | e.g. `2026-03` |
| `from` | DateTime? | generatedAt ≥ |
| `to` | DateTime? | generatedAt ≤ |

**Response item**
```json
{
  "id": 200,
  "publicId": "uuid",
  "societyId": 1,
  "societyName": "Sunshine Apartments",
  "flatId": 10,
  "flatNo": "A-101",
  "period": "2026-03",
  "amount": 3500.00,
  "dueDate": "2026-03-10",
  "statusCode": "paid",
  "paidAmount": 3500.00,
  "balanceAmount": 0.00,
  "generatedAt": "2026-03-01T00:00:00Z",
  "isDeleted": false
}
```

> **UI suggestion:** Table with Status badge (green=paid, amber=partial, red=unpaid), period filter dropdown, society filter. Useful for ops/support monitoring.

---

## 10. Invoices — NEW (SaaS subscription invoices)

These are **platform-level billing invoices** for subscriptions — not society maintenance bills.

### `GET /api/admin/invoices`
| Param | Type | Notes |
|---|---|---|
| `page` | int | Default 1 |
| `pageSize` | int | Default 20 |
| `userId` | long? | |
| `status` | string? | `draft` `sent` `paid` `overdue` `void` |
| `invoiceType` | string? | e.g. `subscription`, `one_time` |
| `from` | DateTime? | createdAt ≥ |
| `to` | DateTime? | createdAt ≤ |

**Response item**
```json
{
  "id": "uuid",
  "userId": 101,
  "userName": "Ravi Kumar",
  "subscriptionId": "uuid",
  "invoiceNumber": "INV-2026-0042",
  "invoiceType": "subscription",
  "amount": 999.00,
  "taxAmount": 179.82,
  "totalAmount": 1178.82,
  "currency": "INR",
  "status": "paid",
  "periodStart": "2026-03-01",
  "periodEnd": "2026-06-01",
  "dueDate": "2026-03-05",
  "paidDate": "2026-03-04T12:00:00Z",
  "paymentMethod": "razorpay",
  "paymentReference": "pay_abc123",
  "createdAt": "2026-03-01T00:00:00Z"
}
```

---

## 11. Platform Settings

### `GET /api/admin/settings`
Returns all key-value platform settings as an array.

### `GET /api/admin/settings/{key}`
Returns a single setting by string key.

### `PUT /api/admin/settings` (Upsert)
```json
{ "key": "trial_period_days", "value": "14", "description": "Default trial period in days" }
```

### `DELETE /api/admin/settings/{key}`

**Response item shape**
```json
{
  "id": 1,
  "key": "trial_period_days",
  "value": "30",
  "description": "Default trial period in days",
  "createdAt": "2025-01-01T00:00:00Z",
  "updatedAt": "2026-01-10T00:00:00Z"
}
```

---

## 12. Suggested Admin Navigation Structure

```
Admin Panel
├── Dashboard
├── Societies
│   ├── List   →  GET /api/admin/societies
│   └── Detail →  GET /api/admin/societies/:id  (stats + subscription badge)
├── Users      →  GET /api/admin/users  + /api/admin/users/:id
├── Subscriptions  →  GET /api/admin/subscriptions  (read-only)
├── Payments       →  GET /api/admin/payments       (read-only)
├── Bills          →  GET /api/admin/bills           (new)
├── Invoices       →  GET /api/admin/invoices         (new)
├── Plans
│   ├── List    →  GET  /api/admin/plans
│   ├── Create  →  POST /api/admin/plans
│   └── Edit    →  PUT  /api/admin/plans/:id  (use isActive:false to deactivate)
└── Platform Settings  →  GET/PUT/DELETE /api/admin/settings
```

---

## 13. TypeScript Types

```ts
// ── Shared ──────────────────────────────────────────────────────────────────
interface ApiResponse<T> {
  succeeded: boolean
  message: string
  data: T
}
interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}

// ── Societies ────────────────────────────────────────────────────────────────
interface AdminSocietySubscriptionSummary {
  id: string
  planName: string
  status: string
  subscribedAmount: number
  currency?: string
  currentPeriodEnd?: string
  trialEnd?: string
}
interface AdminSocietyDto {
  id: number; publicId: string; name: string; address?: string; city?: string
  state?: string; pincode?: string; currency: string; defaultMaintenanceCycle: string
  createdAt: string; updatedAt: string; isDeleted: boolean; deletedAt?: string
  onboardingDate: string                      // "YYYY-MM-DD"
  flatCount: number; activeFlatCount: number
  userCount: number; activeUserCount: number
  activeSubscription?: AdminSocietySubscriptionSummary
}

// ── Users ────────────────────────────────────────────────────────────────────
interface AdminUserDto {
  id: number; publicId: string; societyId: number; societyName?: string
  name: string; email?: string; mobile?: string; username?: string
  roleId: number; isActive: boolean; isDeleted: boolean
  lastLogin?: string; createdAt: string
  subscriptionStatus?: string; subscriptionPlan?: string
  trialEndsDate?: string; nextBillingDate?: string
}

// ── Subscriptions ────────────────────────────────────────────────────────────
interface AdminSubscriptionDto {
  id: string; userId: number; userName: string; userEmail?: string
  planId: string; planName: string; status: string
  subscribedAmount: number; currency?: string
  currentPeriodStart?: string; currentPeriodEnd?: string
  trialStart?: string; trialEnd?: string
  cancelledAt?: string; createdAt?: string; updatedAt?: string
}

// ── Payments ─────────────────────────────────────────────────────────────────
interface AdminPaymentDto {
  id: number; publicId: string; societyId: number
  billId?: number; flatId?: number; amount: number
  datePaid?: string; modeCode?: string; reference?: string
  paymentType?: string; razorpayPaymentId?: string
  verifiedAt?: string; isDeleted: boolean; createdAt: string
}

// ── Bills ────────────────────────────────────────────────────────────────────
interface AdminBillDto {
  id: number; publicId: string; societyId: number; societyName?: string
  flatId: number; flatNo?: string; period: string; amount: number
  dueDate?: string; statusCode: string
  paidAmount?: number; balanceAmount?: number
  generatedAt: string; isDeleted: boolean
}

// ── Invoices ─────────────────────────────────────────────────────────────────
interface AdminInvoiceDto {
  id: string; userId: number; userName?: string; subscriptionId?: string
  invoiceNumber: string; invoiceType: string; amount: number
  taxAmount?: number; totalAmount: number; currency?: string; status: string
  periodStart?: string; periodEnd?: string; dueDate: string
  paidDate?: string; paymentMethod?: string; paymentReference?: string
  createdAt?: string
}

// ── Plans ────────────────────────────────────────────────────────────────────
interface AdminPlanDto {
  id: string; name: string; monthlyAmount: number; currency: string
  isActive?: boolean; durationMonths: number; createdAt?: string
}
interface AdminPlanCreateRequest {
  name: string; monthlyAmount: number; currency: string; durationMonths: number
}
interface AdminPlanUpdateRequest extends AdminPlanCreateRequest {
  isActive?: boolean
}

// ── Platform Settings ────────────────────────────────────────────────────────
interface PlatformSettingDto {
  id: number; key: string; value?: string; description?: string
  createdAt: string; updatedAt: string
}
interface PlatformSettingUpsertRequest {
  key: string; value?: string; description?: string
}
```
