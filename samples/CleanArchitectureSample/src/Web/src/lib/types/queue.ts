export interface QueueSummary {
  queueName: string;
  messageType: string;
  handlers: string[];
  group: string | null;
  description: string | null;
  concurrency: number;
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
  activeCount: number;
  inFlightCount: number;
  deadLetterCount: number;
  counterStats: CounterStats | null;
}

export type JobStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed' | 'Cancelled';

export interface JobSummary {
  jobId: string;
  queueName: string;
  messageType: string;
  status: JobStatus;
  progress: number;
  progressMessage: string | null;
  attempt: number;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  lastHeartbeatUtc: string | null;
  errorMessage: string | null;
  /** Captured at enqueue time: tenant and user in this sample. */
  metadata: Record<string, string> | null;
}

export interface JobDashboardView {
  queuedCount: number;
  activeJobs: JobSummary[];
  recentJobs: JobSummary[];
  counterStats: CounterStats | null;
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
}

export interface DeadLetterReplayResult {
  queueName: string;
  replayed: number;
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
  Cancelled: 'bg-yellow-100 text-yellow-800'
};
