export interface QueueSummary {
  queueName: string;
  messageType: string;
  concurrency: number;
  maxRetries: number;
  retryPolicy: string;
  trackProgress: boolean;
  isRunning: boolean;
  messagesProcessed: number;
  messagesFailed: number;
  messagesDeadLettered: number;
  activeCount: number;
  deadLetterCount: number;
  inFlightCount: number;
}

export type JobStatus = 'Queued' | 'Processing' | 'Completed' | 'Failed' | 'Cancelled';

export interface JobSummary {
  jobId: string;
  queueName: string;
  messageType: string;
  status: JobStatus;
  progress: number;
  progressMessage: string | null;
  createdUtc: string;
  startedUtc: string | null;
  completedUtc: string | null;
  errorMessage: string | null;
}

export interface JobDashboardView {
  queuedCount: number;
  activeJobs: JobSummary[];
  recentJobs: JobSummary[];
}

export interface JobCancellationResult {
  jobId: string;
  cancellationRequested: boolean;
}

export interface DemoJobEnqueued {
  jobId: string;
}

export const JOB_STATUS_COLORS: Record<JobStatus, string> = {
  Queued: 'bg-gray-100 text-gray-800',
  Processing: 'bg-blue-100 text-blue-800',
  Completed: 'bg-green-100 text-green-800',
  Failed: 'bg-red-100 text-red-800',
  Cancelled: 'bg-yellow-100 text-yellow-800'
};
