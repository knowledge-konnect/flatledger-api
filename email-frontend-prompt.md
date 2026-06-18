# Frontend Integration Prompt — Email Features (FlatLedger Society App)

## Tech Stack & Conventions
- React 18 + TypeScript, Tailwind CSS + shadcn/ui
- TanStack Query v5, React Hook Form + Zod
- React Router v6, Axios with Bearer token interceptor
- Base URL: `VITE_API_BASE_URL` env var
- All responses: `{ isSuccess: boolean, data: T, message: string }`
- All errors: `{ code: string, message: string, errors?: Record<string, string[]> }`

---

## New Public API Endpoints

```
POST /api/auth/forgot-password     (public, rate-limited: 5/min)
POST /api/auth/reset-password      (public, rate-limited: 5/min)
POST /api/contact-us               (public)
```

---

## 1. Forgot Password Page

**Route:** `/forgot-password` (public, outside ProtectedRoute)

### API

```
POST /api/auth/forgot-password
Body:    { "email": "user@example.com" }
200:     { "isSuccess": true, "message": "If your email exists in our system, you will receive a password reset link." }
400:     { "code": "VALIDATION_ERROR", "errors": { "email": ["..."] } }
429:     { "code": "RATE_LIMIT_EXCEEDED", "message": "Too many requests. Please try again later." }
```

> The API always returns 200 even if the email does not exist — this is intentional to prevent email enumeration.

### Zod Schema

```ts
const forgotPasswordSchema = z.object({
  email: z.string().email("Please enter a valid email address")
})
```

### UI

- Centered card, same style as `/login`
- One field: Email
- Button: "Send Reset Link" — spinner while pending
- On **200**: replace form with success message: _"Check your inbox. If this email is registered, you'll receive a reset link shortly."_ — do not navigate away
- On **400**: show field-level error under email input
- On **429**: show banner: _"Too many attempts. Please try again in a minute."_
- "Back to Login" text link at bottom

### API Helper

```ts
// api/auth.ts
export const forgotPassword = (body: { email: string }) =>
  apiClient.post<ApiResponse<null>>('/api/auth/forgot-password', body)
```

---

## 2. Reset Password Page

**Route:** `/reset-password` (public, outside ProtectedRoute)

Reads `?token=` from the URL. If token is absent, redirect to `/forgot-password` immediately.

### API

```
POST /api/auth/reset-password
Body: {
  "token": "<value from ?token= query param>",
  "newPassword": "NewSecret123!",
  "confirmPassword": "NewSecret123!"
}
200:  { "isSuccess": true, "message": "Password has been reset successfully. You can now login with your new password." }
400:  { "code": "VALIDATION_ERROR", "errors": { "newPassword": ["..."], "confirmPassword": ["..."] } }
401:  { "code": "UNAUTHORIZED", "message": "Invalid or expired reset token." }
```

### Zod Schema

```ts
const resetPasswordSchema = z.object({
  newPassword: z.string()
    .min(8, "Password must be at least 8 characters")
    .regex(/[A-Z]/, "Must contain at least one uppercase letter")
    .regex(/[0-9]/, "Must contain at least one number"),
  confirmPassword: z.string()
}).refine(data => data.newPassword === data.confirmPassword, {
  message: "Passwords do not match",
  path: ["confirmPassword"]
})
```

### UI

- Centered card
- Two fields: "New Password" + "Confirm Password" — each with show/hide toggle
- Token is read from `useSearchParams()` and injected into the request body silently — never shown to the user
- Button: "Reset Password" — spinner while pending
- On **200**: show success toast → navigate to `/login` after 2 seconds
- On **401**: show error banner (not dismissible): _"This reset link is invalid or has expired."_ with a "Request a new one" link to `/forgot-password`
- On **400**: show field-level errors under each input

### API Helper

```ts
// api/auth.ts
export type ResetPasswordRequest = {
  token: string
  newPassword: string
  confirmPassword: string
}

export const resetPassword = (body: ResetPasswordRequest) =>
  apiClient.post<ApiResponse<null>>('/api/auth/reset-password', body)
```

### Login Page Update

Add a "Forgot password?" link below the password field pointing to `/forgot-password`.

```tsx
<Link to="/forgot-password" className="text-sm text-indigo-600 hover:underline">
  Forgot password?
</Link>
```

---

## 3. Contact Us Page

**Route:** `/contact` (public, outside ProtectedRoute)

### API

```
POST /api/contact-us
Body: {
  "name": "Ravi Kumar",           // required
  "email": "ravi@example.com",    // required
  "phone": "9876543210",          // optional
  "subject": "Billing question",  // required
  "message": "I need help with…"  // required
}
200:  { "isSuccess": true, "message": "Thank you for contacting us. We will get back to you soon." }
400:  { "code": "VALIDATION_ERROR", "errors": { ... } }
500:  { "code": "EMAIL_SEND_FAILED", "message": "Failed to send your message. Please try again later." }
```

### Zod Schema

```ts
const contactUsSchema = z.object({
  name: z.string().min(2, "Name must be at least 2 characters").max(100),
  email: z.string().email("Please enter a valid email address"),
  phone: z.string()
    .regex(/^[6-9]\d{9}$/, "Enter a valid 10-digit mobile number")
    .optional()
    .or(z.literal("")),
  subject: z.string().min(3, "Subject is required").max(200),
  message: z.string().min(10, "Message must be at least 10 characters").max(2000)
})
```

### UI

- Form card with fields: Name, Email, Phone (optional), Subject, Message (textarea, 5 rows)
- Character counter under Message: `{length}/2000`
- Button: "Send Message" — spinner while pending
- On **200**: replace form with a thank-you state:
  - Checkmark icon
  - _"Thanks! We'll get back to you within 24 hours."_
  - "Send another message" button to reset the form back to empty
- On **500**: show error toast _"Failed to send. Please try again."_
- On **400**: field-level errors under each input

### API Helper

```ts
// api/contactUs.ts
export type ContactUsRequest = {
  name: string
  email: string
  phone?: string
  subject: string
  message: string
}

export const submitContactUs = (body: ContactUsRequest) =>
  apiClient.post<ApiResponse<null>>('/api/contact-us', body)
```

---

## 4. Register Page — Verify Fields

The welcome email is sent automatically by the backend after `POST /api/auth/register`. No new API call is needed. Verify these fields exist on the register form:

```ts
type RegisterRequest = {
  name: string          // → AdminName in welcome email
  email: string         // → recipient address
  password: string
  societyName: string   // → SocietyName in welcome email — ADD if missing
  societyAddress?: string
}
```

If `societyName` is missing from the register form, add it as a required field with label "Society / Building Name".

---

## 5. Subscription Expiry Banner (Dashboard Layout)

The backend sends reminder emails at 7 days, 1 day, and on expiry. Mirror these in-app with a banner in the main dashboard layout.

### Logic

```tsx
// In DashboardLayout — after fetching subscription data:
const daysUntilExpiry = subscription?.expiryDate
  ? differenceInDays(new Date(subscription.expiryDate), startOfToday())
  : null

// Render banner only when daysUntilExpiry <= 7
```

### Banner Variants

| Days remaining | Background | Message |
|---|---|---|
| 7 | Amber / yellow | "Your subscription expires in 7 days. Renew now to avoid interruption." |
| 1 | Orange | "Your subscription expires tomorrow. Renew now." |
| 0 or expired | Red | "Your subscription has expired. Renew now to restore access." |

Each banner has a **"Renew Now"** button linking to `/subscription/renew`.

### Dismissal Rules

- **7-day banner**: dismissible — store dismissed state in `sessionStorage` so it reappears on next login
- **1-day and expired banners**: not dismissible

```tsx
// sessionStorage key pattern:
const DISMISS_KEY = 'sub_expiry_banner_dismissed'

// Only apply for the 7-day case:
const [dismissed, setDismissed] = useState(
  () => sessionStorage.getItem(DISMISS_KEY) === 'true'
)
```

---

## Router Changes

Add these three routes **outside** the `ProtectedRoute` wrapper:

```tsx
<Route path="/forgot-password" element={<ForgotPasswordPage />} />
<Route path="/reset-password"  element={<ResetPasswordPage />} />
<Route path="/contact"         element={<ContactPage />} />
```

---

## File Checklist

| File | Action |
|------|--------|
| `src/pages/ForgotPasswordPage.tsx` | Create |
| `src/pages/ResetPasswordPage.tsx` | Create |
| `src/pages/ContactPage.tsx` | Create |
| `src/pages/LoginPage.tsx` | Add "Forgot password?" link below password field |
| `src/pages/RegisterPage.tsx` | Verify `societyName` field exists, add if missing |
| `src/api/auth.ts` | Add `forgotPassword` and `resetPassword` exports |
| `src/api/contactUs.ts` | Create with `submitContactUs` export |
| `src/components/layout/DashboardLayout.tsx` | Add subscription expiry banner |
| `src/router.tsx` | Register 3 new public routes |
