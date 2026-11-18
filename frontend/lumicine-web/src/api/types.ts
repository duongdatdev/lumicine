export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  [key: string]: unknown;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// Authentication
export interface UserInfo {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
}

export interface LoginResponse {
  accessToken: string;
  tokenType: string;
  expiresIn: number;
  user: UserInfo;
}

export interface CurrentUserProfile {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
}

// Catalog
export interface GenreSummary {
  id: string;
  name: string;
  slug: string;
}

export interface MovieSummary {
  id: string;
  title: string;
  originalTitle?: string | null;
  synopsis?: string | null;
  durationMinutes: number;
  releaseDate?: string | null;
  ageRating?: string | null;
  posterUrl?: string | null;
  isActive: boolean;
  genres: GenreSummary[];
}

export interface CinemaSummary {
  id: string;
  name: string;
  city: string;
  address: string;
  isActive: boolean;
}

export interface RoomSummary {
  id: string;
  cinemaId: string;
  name: string;
  seatCapacity: number;
  isActive: boolean;
}

export interface ShowtimeSummary {
  id: string;
  movieId: string;
  movieTitle: string;
  moviePosterUrl?: string | null;
  cinemaId: string;
  cinemaName: string;
  cinemaCity: string;
  roomId: string;
  roomName: string;
  startAtUtc: string;
  endAtUtc: string;
  basePrice: number;
  status: string; // "Scheduled" | "Cancelled"
}

// Seats
export interface ShowtimeSeatSummary {
  seatId: string;
  rowLabel: string;
  number: number;
  seatType: string; // "Normal" | "VIP" | "Couple"
  status: string; // "Available" | "Held" | "Sold"
  price: number;
}

export interface RoomSeatLayoutRow {
  rowLabel: string;
  seatType: string;
  seatCount: number;
}

export interface AdminSeatSummary {
  id: string;
  rowLabel: string;
  number: number;
  seatType: string;
  isActive: boolean;
}

// Bookings
export interface BookingSeat {
  seatId: string;
  rowLabel: string;
  number: number;
  seatType: string;
  unitPrice: number;
}

export interface BookingDetails {
  id: string;
  code: string;
  showtimeId: string;
  movieId: string;
  movieTitle: string;
  cinemaId: string;
  cinemaName: string;
  roomId: string;
  roomName: string;
  status: string; // "Pending" | "Confirmed" | "Cancelled" | "Expired"
  holdExpiresAtUtc: string | null;
  totalAmount: number;
  createdAtUtc: string;
  serverTimeUtc: string;
  seats: BookingSeat[];
}

export interface CreateBookingRequest {
  requestId: string;
  showtimeId: string;
  seatIds: string[];
}

export interface BookingPaymentLink {
  bookingId: string;
  paymentId: string;
  requestId: string;
  paymentUrl: string;
  expiresAtUtc: string;
}

// Payments
export interface PaymentView {
  id: string;
  bookingId: string;
  requestId: string;
  merchantReference: string;
  amount: number;
  currency: string;
  status: string; // "Pending" | "Succeeded" | "Failed"
  processingStatus: string;
  paymentUrl?: string | null;
  createdAtUtc: string;
  expiresAtUtc: string;
  processedAtUtc?: string | null;
  vnPayTransactionId?: string | null;
  responseCode?: string | null;
  transactionStatus?: string | null;
}

export interface VnPayReturnResponse {
  rspCode: string;
  message: string;
  payment?: PaymentView | null;
}

export interface AdminPaymentView {
  payment: PaymentView;
  bookingCode: string;
  customerEmail?: string | null;
}

// Tickets
export interface TicketDetails {
  id: string;
  bookingId: string;
  bookingCode: string;
  ticketCode: string;
  status: string; // "Valid" | "Used" | "Cancelled"
  issuedAtUtc: string;
  usedAtUtc?: string | null;
  showtimeId: string;
  movieTitle: string;
  cinemaName: string;
  roomName: string;
  startAtUtc: string;
  endAtUtc: string;
  rowLabel: string;
  number: number;
  seatType: string;
  unitPrice: number;
}

// Discovery AI
export interface DiscoveryFiltersView {
  topicText?: string | null;
  movieNameText?: string | null;
  genreSlugs: string[];
  maxDurationMinutes?: number | null;
  city?: string | null;
  cinemaName?: string | null;
  dateFrom?: string | null;
  dateTo?: string | null;
  timeWindowStartMinutes?: number | null;
  timeWindowEndMinutes?: number | null;
  maxPrice?: number | null;
  minSeats?: number | null;
}

export interface DiscoveryShowtimeResult {
  showtimeId: string;
  movieId: string;
  cinemaId: string;
  cinemaName: string;
  roomId: string;
  roomName: string;
  startAtUtc: string;
  price: number;
  availableSeats: number;
}

export interface DiscoveryMovieResult {
  movieId: string;
  title: string;
  originalTitle?: string | null;
  durationMinutes: number;
  ageRating?: string | null;
  posterUrl?: string | null;
  genres: string[];
  showtimes: DiscoveryShowtimeResult[];
  score: number;
}

export interface DiscoveryChatResponse {
  conversationId: string;
  version: number;
  requestId: string;
  turnNumber: number;
  answer: string;
  appliedFilters: DiscoveryFiltersView;
  movies: DiscoveryMovieResult[];
  mode: string;
  isDegraded: boolean;
  warnings: string[];
  generatedAtUtc: string;
  availabilityAsOfUtc: string;
}

export interface DiscoveryMessageView {
  turnNumber: number;
  role: string; // "user" | "assistant"
  content: string;
  createdAtUtc: string;
  response?: DiscoveryChatResponse | null;
}

export interface DiscoveryConversationSummary {
  id: string;
  version: number;
  turnCount: number;
  isExpired: boolean;
  createdAtUtc: string;
  lastActiveAtUtc: string;
  expiresAtUtc: string;
}

export interface DiscoveryConversationDetails {
  id: string;
  version: number;
  turnCount: number;
  isExpired: boolean;
  filters: DiscoveryFiltersView;
  messages: DiscoveryMessageView[];
  createdAtUtc: string;
  lastActiveAtUtc: string;
  expiresAtUtc: string;
}

// Staff
export interface CheckInRequest {
  requestId: string;
  ticketCode: string;
}

export interface CheckInResult {
  requestId: string;
  outcome: string; // "Success" | "DuplicateScan" | "TicketNotFound" | "BookingNotConfirmed" | "ShowtimeCancelled" | "OutsideWindow" | "Rejected"
  ticketId?: string | null;
  ticketCode?: string | null;
  bookingId?: string | null;
  bookingCode?: string | null;
  showtimeId?: string | null;
  usedAtUtc?: string | null;
  windowStartsAtUtc?: string | null;
  windowEndsAtUtc?: string | null;
  serverTimeUtc: string;
  ticket?: Pick<TicketDetails, 'movieTitle' | 'cinemaName' | 'roomName' | 'startAtUtc' | 'rowLabel' | 'number' | 'seatType'> | null;
}

// Admin Reporting
export interface ReportMetadata {
  fromDate: string;
  toDate: string;
  generatedAtUtc: string;
  movieId?: string | null;
  cinemaId?: string | null;
}

export interface AdminDashboardSummary {
  metadata: ReportMetadata;
  ticketSalesAmount: number;
  collectedAmount: number;
  recordedRefundAmount: number;
  netCollectedAmount: number;
  successfulPaymentCount: number;
  failedPaymentCount: number;
  paymentsMissingRequiredTimestampCount: number;
  bookingsCreatedCount: number;
  bookingStatuses: Record<string, number>;
  ticketsIssuedCount: number;
  ticketsUsedCount: number;
  rejectedCheckInCount: number;
  rejectedCheckIns: Record<string, number>;
}

export interface AdminDashboardOperations {
  asOfUtc: string;
  movieId?: string | null;
  cinemaId?: string | null;
  refundReviewCount: number;
  refundReviewAmount: number;
  refundReviewPaymentIds: string[];
  failedEmailCount: number;
  failedEmailIds: string[];
  dueEmailCount: number;
  dueEmailIds: string[];
  pendingIndexCount: number;
  pendingIndexMovieIds: string[];
  failedIndexCount: number;
  failedIndexMovieIds: string[];
  paymentsMissingProcessedAtCount: number;
  paymentsMissingProcessedAtIds: string[];
}

export interface RevenueReportRow {
  key: string;
  label: string;
  ticketSalesAmount: number;
  collectedAmount: number;
  recordedRefundAmount: number;
  netCollectedAmount: number;
  successfulPaymentCount: number;
  failedPaymentCount: number;
}

export interface RevenueReportTotals {
  ticketSalesAmount: number;
  collectedAmount: number;
  recordedRefundAmount: number;
  netCollectedAmount: number;
  successfulPaymentCount: number;
  failedPaymentCount: number;
}

export interface ShowtimeReportRow {
  showtimeId: string;
  movieId: string;
  movieTitle: string;
  cinemaId: string;
  cinemaName: string;
  roomId: string;
  roomName: string;
  startAtUtc: string;
  endAtUtc: string;
  status: string;
  capacity: number;
  soldSeats: number;
  heldSeats: number;
  availableSeats: number;
  occupancyRate: number;
  validTickets: number;
  usedTickets: number;
  checkInRate: number;
  issuedTicketAmount: number;
}

export interface ShowtimeReportTotals {
  capacity: number;
  soldSeats: number;
  heldSeats: number;
  availableSeats: number;
  occupancyRate: number;
  validTickets: number;
  usedTickets: number;
  checkInRate: number;
  issuedTicketAmount: number;
}

export interface AdminReportResult<TRow, TTotals> {
  metadata: ReportMetadata;
  rows: TRow[];
  totalCount: number;
  page: number;
  pageSize: number;
  totals: TTotals;
}

// Admin Users
export interface AdminUserSummary {
  id: string;
  email: string;
  displayName: string;
  roles: string[];
  isActive: boolean;
  emailConfirmed: boolean;
  createdAtUtc: string;
}

export interface AdminUserPage {
  users: AdminUserSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
}

// Admin Discovery
export interface DiscoveryIndexStatus {
  movieCount: number;
  embeddedMovieCount: number;
  isStale: boolean;
  lastBuiltAtUtc?: string | null;
}

export interface DiscoveryRebuildResult {
  indexedCount: number;
  completedAtUtc: string;
}
