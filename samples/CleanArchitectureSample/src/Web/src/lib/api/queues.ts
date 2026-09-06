import { api } from './client';
import type {
  QueueSummary,
  JobSummary,
  JobDashboardView,
  JobCancellationResult,
  DeadLetterView,
  DeadLetterReplayResult,
  DeadLetterPurgeResult,
  HostInfoView,
  EnqueueReceipt
} from '$lib/types/queue';

const q = (name: string) => encodeURIComponent(name);

export const queuesApi = {
  // Reads (anonymous)
  list: () => api.getJSON<QueueSummary[]>('/api/queues/queues'),

  get: (queueName: string) =>
    api.getJSON<QueueSummary>(`/api/queues/queue?queueName=${q(queueName)}`),

  host: () => api.getJSON<HostInfoView>('/api/queues/host'),

  getJobDashboard: (
    queueName: string,
    status = 'active',
    skip = 0,
    take = 25
  ) =>
    api.getJSON<JobDashboardView>(
      `/api/queues/job-dashboard?queueName=${q(queueName)}&status=${q(status)}&skip=${skip}&take=${take}`
    ),

  getJob: (jobId: string) =>
    api.getJSON<JobSummary>(`/api/queues/queue-job/${q(jobId)}`),

  getDeadLetters: (queueName: string, take: number = 20) =>
    api.getJSON<DeadLetterView[]>(
      `/api/queues/dead-letters?queueName=${q(queueName)}&take=${take}`
    ),

  // Operations (Admin role)
  cancelJob: (jobId: string) =>
    api.postJSON<JobCancellationResult>(
      `/api/queues/job/${q(jobId)}/cancel-job`,
      {}
    ),

  replayDeadLetters: (queueName: string, messageId?: string, max = 100) =>
    api.postJSON<DeadLetterReplayResult>('/api/queues/dead-letters/replay', {
      queueName,
      max,
      messageId: messageId ?? null
    }),

  purgeDeadLetters: (queueName: string, max = 1000) =>
    api.postJSON<DeadLetterPurgeResult>('/api/queues/dead-letters/purge', {
      queueName,
      max
    }),

  enqueueExports: (
    count = 1,
    steps = 20,
    stepDelayMs = 1500,
    failTimes = 0,
    criticalFailure = false
  ) =>
    api.postJSON<EnqueueReceipt>('/api/queues/enqueue/exports', {
      count,
      steps,
      stepDelayMs,
      failTimes,
      criticalFailure
    }),

  enqueueImports: (count = 1, rows = 200, rowDelayMs = 50) =>
    api.postJSON<EnqueueReceipt>('/api/queues/enqueue/imports', {
      count,
      rows,
      rowDelayMs
    }),

  enqueueFlakyWebhook: (url: string, failTimes: number) =>
    api.postJSON<EnqueueReceipt>('/api/queues/enqueue/flaky-webhook', {
      url,
      failTimes
    }),

  enqueueBankFiles: (bank: string, count = 2) =>
    api.postJSON<EnqueueReceipt>('/api/queues/enqueue/bank-files', {
      bank,
      count
    }),

  /** Direct invocation answers 202 Accepted; use enqueueExports for typed job receipts. */
  enqueueExportDirect: (steps = 20, stepDelayMs = 1500) =>
    api.postJSON<void>('/api/export-jobs/demo', { steps, stepDelayMs })
};
