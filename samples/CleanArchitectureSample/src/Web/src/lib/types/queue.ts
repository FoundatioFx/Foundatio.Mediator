export interface QueueSummary {
  queueName: string;
  displayName: string | null;
  messageType: string;
  handlers: string[];
  group: string | null;
  description: string | null;
  concurrency: number;
  prefetchCount: number;
  autoComplete: boolean;
  autoRenewTimeout: boolean;
  retryDelays: string | null;
  maxAttempts: number;
  retryPolicy: string;
  visibilityTimeoutSeconds: number;
  trackProgress: boolean;
  /** Whether the node that answered the request runs a worker for this queue. */
  workerRunsHere: boolean;
  isRunning: boolean | null;
  messagesProcessed: number;
  messagesFailed: number;
  messagesDeadLettered: number;
  statisticsAvailable: boolean;
  activeCount: number;
  delayedCount: number;
  inFlightCount: number;
  deadLetterCount: number;
  counterStats: CounterStats | null;
}

export type JobStatus =
  | 'Queued'
  | 'Processing'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'RetryPending'
  | 'EnqueueUnknown';

export interface JobSummary {
  jobId: string;
  queueName: string;
  messageType: string;
  status: JobStatus;
  progress: number;
  progressMessage: string | null;
  attempt: number;
  workerId: string | null;
  cancellationRequested: boolean;
  lastUpdatedUtc: string;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  lastHeartbeatUtc: string | null;
  errorMessage: string | null;
  /** Captured at enqueue time: tenant and user in this sample. */
  metadata: Record<string, string> | null;
}

export interface JobDashboardView {
  counts: Record<JobStatus, number>;
  jobs: JobSummary[];
  total: number;
  skip: number;
  take: number;
  updatedUtc: string;
}

export interface CounterStats {
  totals: Record<string, number>;
  buckets: CounterBucket[];
}

export interface CounterBucket {
  hour: string;
  counters: Record<string, number>;
}

export interface JobCancellationResult {
  jobId: string;
  cancellationRequested: boolean;
}

export interface DeadLetterView {
  messageId: string;
  queueName: string;
  originalQueueName: string | null;
  messageType: string | null;
  reason: string | null;
  deadLetteredAt: string | null;
  attempts: number | null;
  jobId: string | null;
  correlationId: string | null;
  body: string;
  bodyTruncated: boolean;
  headers: Record<string, string>;
}

export interface DeadLetterReplayResult {
  queueName: string;
  replayed: number;
  receipts: { queueName: string; jobId: string | null }[];
  skipped: number;
}

export interface DeadLetterPurgeResult {
  queueName: string;
  purged: number;
}

export interface HostInfoView {
  hostId: string;
  workers: string;
}

export interface EnqueueReceipt {
  queueName: string;
  count: number;
  jobIds: string[];
}

export const JOB_STATUS_COLORS: Record<JobStatus, string> = {
  Queued: 'bg-gray-100 text-gray-800',
  Processing: 'bg-blue-100 text-blue-800',
  Completed: 'bg-green-100 text-green-800',
  Failed: 'bg-red-100 text-red-800',
  Cancelled: 'bg-yellow-100 text-yellow-800',
  RetryPending: 'bg-orange-100 text-orange-800',
  EnqueueUnknown: 'bg-purple-100 text-purple-800'
};

export const JOB_STATUSES: JobStatus[] = [
  'Queued',
  'Processing',
  'RetryPending',
  'EnqueueUnknown',
  'Completed',
  'Failed',
  'Cancelled'
];
export const statusLabel = (status: string) =>
  status === 'RetryPending'
    ? 'Waiting for retry'
    : status === 'EnqueueUnknown'
      ? 'Acceptance unknown'
      : status;
export const isTerminal = (status: JobStatus) =>
  ['Completed', 'Failed', 'Cancelled'].includes(status);
export const formatTime = (value: string | null) =>
  value ? new Date(value).toLocaleString() : '—';
export function elapsed(
  start: string | null,
  end: string | null = null,
  now = Date.now()
): string {
  if (!start) return '—';
  const seconds = Math.max(
    0,
    Math.floor(
      ((end ? new Date(end).getTime() : now) - new Date(start).getTime()) / 1000
    )
  );
  return seconds < 60
    ? `${seconds}s`
    : `${Math.floor(seconds / 60)}m ${seconds % 60}s`;
}
