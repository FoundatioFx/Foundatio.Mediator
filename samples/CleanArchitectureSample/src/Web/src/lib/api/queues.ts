import { api } from './client';
import type { QueueSummary, JobSummary, JobDashboardView, JobCancellationResult, DemoJobEnqueued } from '$lib/types/queue';

export const queuesApi = {
  listWorkers: () => api.getJSON<QueueSummary[]>('/api/queues/queues'),

  getWorker: (queueName: string) =>
    api.getJSON<QueueSummary>(`/api/queues/queue?queueName=${encodeURIComponent(queueName)}`),

  getJobDashboard: (queueName: string, recentTerminalCount: number = 5) =>
    api.getJSON<JobDashboardView>(
      `/api/queues/job-dashboard?queueName=${encodeURIComponent(queueName)}&recentTerminalCount=${recentTerminalCount}`
    ),

  getJob: (jobId: string) => api.getJSON<JobSummary>(`/api/queues/queue-job/${jobId}`),

  cancelJob: (jobId: string) =>
    api.postJSON<JobCancellationResult>(`/api/queues/job/${jobId}/cancel-job`, {}),

  enqueueDemoJob: (count = 1, steps = 10, stepDelayMs = 500) =>
    api.postJSON<DemoJobEnqueued>('/api/queues/demo-job/enqueue-demo-job', { count, steps, stepDelayMs }),

  /** Call the DemoExportJob queue endpoint directly — the mediator enqueues it async and returns 202 Accepted. */
  enqueueDemoJobDirect: (steps = 20, stepDelayMs = 1500) =>
    api.postJSON<void>('/api/export-jobs/demo', { steps, stepDelayMs })
};
