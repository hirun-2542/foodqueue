// Wire types for the FoodQueue API. JSON is camelCase, enums are strings,
// money is a JSON number (decimal(10,2) on the server), times are ISO-8601 UTC strings.

export type OrderStatus = "Pending" | "Preparing" | "Ready" | "Cancelled";
export type MenuItemKind = "Buffet" | "AlaCarte";
export type SessionStatus = "Open" | "Paid" | "Voided";
export type PaymentMethod = "Cash" | "PromptPay" | "Card";
export type StaffRole = "Owner" | "Kitchen";

export const API_ERROR_CODES = [
  "SESSION_NOT_FOUND",
  "SESSION_CLOSED",
  "SESSION_EXPIRED",
  "TABLE_OCCUPIED",
  "ITEM_NOT_FOUND",
  "ITEM_UNAVAILABLE",
  "ITEM_NOT_IN_PACKAGE",
  "QTY_LIMIT_EXCEEDED",
  "ORDER_ITEMS_LIMIT_EXCEEDED",
  "EMPTY_ORDER",
  "INVALID_STATUS_TRANSITION",
  "ORDER_NOT_EDITABLE",
  "ORDERS_IN_PROGRESS",
  "NEGATIVE_SUBTOTAL",
  "SESSION_HAS_ORDERS",
  "ALREADY_PAID",
  "INSUFFICIENT_AMOUNT",
  "VALIDATION_FAILED",
] as const;

export type ApiErrorCode = (typeof API_ERROR_CODES)[number];

/** ASP.NET Core ProblemDetails plus the FoodQueue `code` / Thai `message` extensions. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  message?: string;
}

export interface MeDto {
  email: string;
  displayName: string;
  role: StaffRole;
}

export interface OrderItemDto {
  name: string;
  kind: MenuItemKind;
  quantity: number;
  unitPrice: number;
  note: string | null;
}

export interface OrderDto {
  id: number;
  sessionId: number;
  tableName: string;
  number: number;
  status: OrderStatus;
  cancelReason?: string | null;
  createdAt: string;
  updatedAt: string;
  items: OrderItemDto[];
}

export interface BillAlaCarteLineDto {
  name: string;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface BillAdjustmentDto {
  id: number;
  description: string;
  amount: number;
}

export interface BillDto {
  adults: number;
  children: number;
  adultPrice: number;
  childPrice: number;
  buffetTotal: number;
  alaCarteLines: BillAlaCarteLineDto[];
  alaCarteTotal: number;
  adjustments: BillAdjustmentDto[];
  adjustmentTotal: number;
  subtotal: number;
  serviceChargePercent: number;
  serviceCharge: number;
  vatPercent: number;
  vat: number;
  total: number;
}

export interface PaymentDto {
  method: PaymentMethod;
  amountReceived: number;
  change: number;
  receiptNo: string;
  paidAt: string;
  total: number;
}

export interface SessionDetailDto {
  id: number;
  tableId: number;
  tableName: string;
  packageName: string;
  packageRank: number;
  adults: number;
  children: number;
  openedAt: string;
  endsAt: string;
  status: SessionStatus;
  qrUrl: string;
  isDemoBot: boolean;
  bill: BillDto;
  orders: OrderDto[];
  adjustments: BillAdjustmentDto[];
  payment?: PaymentDto | null;
}

export interface SessionSummaryDto {
  id: number;
  tableId: number;
  tableName: string;
  packageName: string;
  adults: number;
  children: number;
  openedAt: string;
  endsAt: string;
  status: SessionStatus;
}

export interface TableDto {
  id: number;
  name: string;
  seats: number;
  zone: string | null;
  sortOrder: number;
  isActive: boolean;
}

export interface TableOverviewDto {
  id: number;
  name: string;
  zone: string | null;
  seats: number;
  isActive: boolean;
  sessionId?: number | null;
  packageName?: string | null;
  adults?: number | null;
  children?: number | null;
  openedAt?: string | null;
  endsAt?: string | null;
  minutesUsed?: number | null;
  ordersWaiting?: number | null;
  runningTotal?: number | null;
}

/** Payload of the `menuItemChanged` hub event. */
export interface MenuItemChangedEvent {
  menuItemId: number;
  isAvailable: boolean;
  isActive: boolean;
}

/** Payload of the `sessionClosed` hub event. */
export interface SessionClosedEvent {
  sessionId: number;
}

/** Every server → client event on `/hubs/live`, keyed by event name. */
export interface LiveEvents {
  orderCreated: OrderDto;
  orderStatusChanged: OrderDto;
  orderEdited: OrderDto;
  sessionChanged: SessionSummaryDto;
  menuItemChanged: MenuItemChangedEvent;
  sessionClosed: SessionClosedEvent;
}

/** A buffet tier (`GET /api/packages`). */
export interface PackageDto {
  id: number;
  name: string;
  rank: number;
  adultPrice: number;
  childPrice: number;
  durationMinutes: number;
  isActive: boolean;
}

/** Body of `POST /api/packages` and `PUT /api/packages/{id}`. */
export type PackageInput = Omit<PackageDto, "id">;

/**
 * Shop-wide settings (`GET /api/settings`). `bannerImagePath` is a public path under `/api/uploads/`
 * (or null = the built-in banner); it is never settable through the PUT, only through the banner upload.
 * `serviceChargePercent` / `vatPercent` are 0–30, the limits are 1–99.
 */
export interface SettingsDto {
  name: string;
  address: string;
  phone: string;
  taxId?: string | null;
  promptPayId?: string | null;
  bannerImagePath?: string | null;
  serviceChargePercent: number;
  vatPercent: number;
  maxQtyPerItemPerOrder: number;
  maxItemsPerOrder: number;
  lastOrderWarningMinutes: number;
}

/** A menu category (`GET /api/categories`); `itemCount` counts every item in it. */
export interface CategoryDto {
  id: number;
  name: string;
  sortOrder: number;
  itemCount: number;
}

/** Body of `POST /api/categories` and `PUT /api/categories/{id}`. */
export interface CategoryInput {
  name: string;
  sortOrder?: number;
}

/**
 * A menu item (`GET /api/menu-items`). `kind` is `Buffet` (needs `minPackageRank`, price 0) or
 * `AlaCarte` (no `minPackageRank`, price > 0) — the API enforces both.
 */
export interface MenuItemDto {
  id: number;
  categoryId: number;
  categoryName: string;
  name: string;
  description?: string | null;
  imagePath?: string | null;
  kind: MenuItemKind;
  minPackageRank?: number | null;
  price: number;
  isAvailable: boolean;
  isActive: boolean;
  sortOrder: number;
}

/** Body of `POST /api/menu-items` and `PUT /api/menu-items/{id}`; the image has its own endpoint. */
export interface MenuItemInput {
  name: string;
  categoryId: number;
  description?: string | null;
  kind: MenuItemKind;
  minPackageRank?: number | null;
  price: number;
  isAvailable?: boolean;
  isActive?: boolean;
  sortOrder?: number;
}
