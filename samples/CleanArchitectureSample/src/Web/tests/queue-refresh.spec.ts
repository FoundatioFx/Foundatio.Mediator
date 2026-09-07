import { test, expect, type Page } from '@playwright/test';
import type {
  DeadLetterView,
  JobSummary,
  QueueSummary
} from '../src/lib/types/queue';

const queueName = 'sample-DemoExportJob';
const detailUrl = `/queues/${queueName}`;
const original: DeadLetterView = {
  messageId: 'original-message',
  queueName,
  originalQueueName: queueName,
  messageType: 'DemoExportJob',
  reason: 'Simulated unrecoverable failure',
  deadLetteredAt: '2026-09-07T06:00:00Z',
  attempts: 1,
  jobId: 'original-job',
  correlationId: null,
  body: '{"CriticalFailure":true}',
  bodyTruncated: false,
  headers: {}
};
const running: JobSummary = {
  jobId: 'running-job',
  queueName,
  messageType: 'DemoExportJob',
  status: 'Processing',
  progress: 20,
  progressMessage: 'Exporting',
  attempt: 1,
  workerId: 'worker-1',
  cancellationRequested: false,
  lastUpdatedUtc: '2026-09-07T06:00:00Z',
  createdUtc: '2026-09-07T06:00:00Z',
  startedUtc: '2026-09-07T06:00:00Z',
  completedUtc: null,
  lastHeartbeatUtc: '2026-09-07T06:00:00Z',
  errorMessage: null,
  metadata: null
};

function gate() {
  let release!: () => void;
  const promise = new Promise<void>((resolve) => {
    release = resolve;
  });
  return { promise, release };
}

async function fixture(page: Page) {
  const queues: QueueSummary[] = await (
    await page.request.get('/api/queues/queues')
  ).json();
  const queue = queues.find((q) => q.queueName === queueName)!;
  const state = {
    queue,
    letters: [original],
    job: { ...running },
    reads: 0,
    inspections: 0,
    hold: null as ReturnType<typeof gate> | null,
    inspectionHold: null as ReturnType<typeof gate> | null,
    fail: false
  };
  await page.route('**/api/auth/current-user', (route) =>
    route.fulfill({
      json: {
        displayName: 'Test operator',
        username: 'admin',
        role: 'Admin'
      }
    })
  );
  await page.route('**/api/queues/**', async (route) => {
    const url = new URL(route.request().url());
    if (
      url.pathname === '/api/queues/queues' ||
      url.pathname === '/api/queues/queue'
    ) {
      state.reads++;
      await state.hold?.promise;
      if (state.fail)
        return route.fulfill({ status: 503, json: { title: 'Offline' } });
      return route.fulfill({
        json: url.pathname.endsWith('/queues') ? [state.queue] : state.queue
      });
    }
    if (url.pathname === '/api/queues/host')
      return route.fulfill({ json: { hostId: 'test-api', workers: 'None' } });
    if (url.pathname === '/api/queues/dead-letters') {
      state.inspections++;
      await state.inspectionHold?.promise;
      return route.fulfill({ json: state.letters });
    }
    if (url.pathname === '/api/queues/job-dashboard')
      return route.fulfill({
        json: {
          counts: {
            Queued: 0,
            Processing: 30,
            RetryPending: 0,
            EnqueueUnknown: 0,
            Completed: 0,
            Failed: 0,
            Cancelled: 0
          },
          jobs: [state.job],
          total: 30,
          skip: Number(url.searchParams.get('skip')),
          take: 25,
          updatedUtc: state.job.lastUpdatedUtc
        }
      });
    if (url.pathname.startsWith('/api/queues/queue-job/'))
      return route.fulfill({ json: state.job });
    // Never allow a mocked test to mutate the real sample.
    return route.fulfill({
      status: 500,
      json: { title: `Unexpected request: ${url.pathname}` }
    });
  });
  return state;
}

for (const view of ['dashboard', 'overview', 'jobs', 'dead-letters'] as const) {
  test(`${view} background updates retain controls, focus, and DOM while reads are slow`, async ({
    page
  }) => {
    const state = await fixture(page);
    await page.goto(
      view === 'dashboard' ? '/queues' : `${detailUrl}?view=${view}`
    );
    const refresh = page.getByRole('button', {
      name: 'Refresh now',
      exact: true
    });
    await expect(refresh).toBeEnabled();
    await expect(
      page.getByRole('heading', {
        name: view === 'dashboard' ? 'Subscriptions' : 'Export jobs',
        exact: true
      })
    ).toBeVisible();
    const retained =
      view === 'dashboard'
        ? page.getByRole('table')
        : view === 'overview'
          ? page.getByRole('group', {
              name: `Hourly queue activity for ${queueName}`
            })
          : view === 'jobs'
            ? page.locator('[data-job-id="running-job"]')
            : page.locator('[data-message-id="original-message"]');
    await expect(retained).toBeVisible();
    if (view === 'dead-letters')
      await retained
        .getByRole('button', { name: 'Inspect DemoExportJob' })
        .click();
    const focus =
      view === 'dashboard'
        ? page.getByRole('textbox', { name: 'Filter queues' })
        : view === 'jobs'
          ? page.getByRole('textbox', { name: 'Find job by ID' })
          : view === 'dead-letters'
            ? retained.getByRole('button', { name: 'Retry message' })
            : retained.getByRole('button').last();
    await focus.focus();
    const element = await retained.elementHandle();
    const position = await refresh.boundingBox();
    const reads = state.reads;
    state.hold = gate();
    await expect.poll(() => state.reads).toBeGreaterThan(reads);
    await expect(refresh).toBeEnabled();
    await expect(focus).toBeFocused();
    if (view === 'jobs')
      await expect(
        page.getByRole('button', { name: 'Next', exact: true })
      ).toBeEnabled();
    if (view === 'dead-letters') {
      await expect(focus).toBeEnabled();
      await expect(
        retained.getByRole('heading', { name: 'Payload', exact: true })
      ).toBeVisible();
      expect(state.inspections).toBe(1);
    }
    expect(await refresh.boundingBox()).toEqual(position);
    state.queue = { ...state.queue, activeCount: 4321 };
    state.job = { ...state.job, progress: 45 };
    const response = page.waitForResponse((r) =>
      ['/api/queues/queue', '/api/queues/queues'].includes(
        new URL(r.url()).pathname
      )
    );
    state.hold.release();
    state.hold = null;
    await response;
    await expect(page.getByText('4,321', { exact: true })).toBeVisible();
    expect(
      await retained.evaluate((node, previous) => node === previous, element)
    ).toBe(true);
    await expect(focus).toBeFocused();
    if (view === 'jobs')
      await expect(retained.getByRole('progressbar')).toHaveAttribute(
        'value',
        '45'
      );
    if (view === 'dead-letters') expect(state.inspections).toBe(1);
  });
}

test('retry removes the selected row before inspection completes and distinguishes a newly failed retry', async ({
  page
}) => {
  const state = await fixture(page);
  state.letters = [original, { ...original, messageId: 'unrelated-message' }];
  await page.route('**/api/queues/dead-letters/replay', async (route) => {
    expect(route.request().postDataJSON().messageId).toBe(original.messageId);
    state.letters = [
      state.letters[1],
      {
        ...original,
        messageId: 'new-failure',
        jobId: 'retry-job',
        deadLetteredAt: '2026-09-07T07:00:00Z',
        headers: {
          'fm-original-job-id': 'original-job',
          'fm-replayed-at': '2026-09-07T07:00:00Z'
        }
      }
    ];
    state.job = {
      ...running,
      jobId: 'retry-job',
      status: 'Failed',
      errorMessage: 'Simulated unrecoverable failure'
    };
    state.inspectionHold = gate();
    return route.fulfill({
      json: {
        queueName,
        replayed: 1,
        skipped: 0,
        receipts: [{ queueName, jobId: 'retry-job' }]
      }
    });
  });
  await page.goto(`${detailUrl}?view=dead-letters`);
  const row = page.locator('[data-message-id="original-message"]');
  await row.getByRole('button', { name: 'Retry message' }).click();
  await expect(row).toHaveCount(0);
  await expect(
    page.locator('[data-message-id="unrelated-message"]')
  ).toBeVisible();
  await expect(page.getByLabel('Retry result')).toContainText(
    'returned to the normal queue'
  );
  state.inspectionHold!.release();
  state.inspectionHold = null;
  await expect(page.getByLabel('Retry result')).toContainText('Failed again');
  const failure = page.locator('[data-message-id="new-failure"]');
  await expect(failure).toContainText('Failed again after retry');
  await expect(
    failure.getByRole('button', { name: 'Previous job', exact: true })
  ).toBeVisible();
  await expect(
    failure.getByRole('button', { name: 'Failed job', exact: true })
  ).toBeVisible();
});

test('zero or failed replay keeps the selected record visible', async ({
  page
}) => {
  await fixture(page);
  let fail = false;
  await page.route('**/api/queues/dead-letters/replay', (route) =>
    route.fulfill(
      fail
        ? { status: 503, json: { title: 'Transport unavailable' } }
        : { json: { queueName, replayed: 0, skipped: 1, receipts: [] } }
    )
  );
  await page.goto(`${detailUrl}?view=dead-letters`);
  const row = page.locator('[data-message-id="original-message"]');
  await row.getByRole('button', { name: 'Retry message' }).click();
  await expect(
    page.getByText(/No matching available messages were replayed/)
  ).toBeVisible();
  await expect(row).toBeVisible();
  fail = true;
  await row.getByRole('button', { name: 'Retry message' }).click();
  await expect(
    page.getByText('Transport unavailable', { exact: true })
  ).toBeVisible();
  await expect(row).toBeVisible();
  await expect(page.getByLabel('Retry result')).toHaveCount(0);
});

test('manual refresh during a poll coalesces reads and preserves an open job inspector', async ({
  page
}) => {
  const state = await fixture(page);
  await page.goto(`${detailUrl}?view=jobs&job=running-job`);
  const dialog = page.getByRole('dialog', { name: 'Job details' });
  await expect(dialog.getByRole('progressbar')).toHaveAttribute('value', '20');
  const element = await dialog.elementHandle();
  const close = dialog.getByRole('button', { name: 'Close', exact: true });
  await close.focus();
  const reads = state.reads;
  state.hold = gate();
  await expect.poll(() => state.reads).toBeGreaterThan(reads);
  await expect(close).toBeFocused();
  state.job = { ...state.job, progress: 60 };
  state.hold.release();
  state.hold = null;
  await expect(dialog.getByRole('progressbar')).toHaveAttribute('value', '60');
  expect(
    await dialog.evaluate((node, previous) => node === previous, element)
  ).toBe(true);
  await expect(close).toBeFocused();
  await close.click();
  await page
    .getByRole('button', { name: 'Pause updates', exact: true })
    .click();
  const baseline = state.reads;
  state.hold = gate();
  await page
    .getByRole('button', { name: 'Resume updates', exact: true })
    .click();
  await expect.poll(() => state.reads).toBeGreaterThan(baseline);
  await page
    .getByRole('button', { name: 'Pause updates', exact: true })
    .click();
  await page.getByRole('button', { name: 'Refresh now', exact: true }).click();
  await expect(
    page.getByRole('button', { name: 'Refresh now', exact: true })
  ).toBeDisabled();
  expect(state.reads).toBe(baseline + 1);
  state.hold.release();
  state.hold = null;
  await expect(
    page.getByRole('button', { name: 'Refresh now', exact: true })
  ).toBeEnabled();
  expect(state.reads).toBe(baseline + 2);
});

test('rapid job filters retain the previous view until the latest response and ignore stale results', async ({
  page
}) => {
  await fixture(page);
  await page.goto(`${detailUrl}?view=jobs`);
  const originalRow = page.locator('[data-job-id="running-job"]');
  await expect(originalRow).toBeVisible();
  await page
    .getByRole('button', { name: 'Pause updates', exact: true })
    .click();
  const hold = gate();
  let pending = false;
  await page.route('**/api/queues/job-dashboard?**', async (route) => {
    const status = new URL(route.request().url()).searchParams.get('status');
    if (status === 'Completed') {
      pending = true;
      await hold.promise;
    }
    return route.fulfill({
      json: {
        counts: {
          Queued: 0,
          Processing: 30,
          RetryPending: 0,
          EnqueueUnknown: 0,
          Completed: 1,
          Failed: 1,
          Cancelled: 0
        },
        jobs: [{ ...running, jobId: `${status}-job`, status }],
        total: 1,
        skip: 0,
        take: 25,
        updatedUtc: running.lastUpdatedUtc
      }
    });
  });
  await page.getByRole('button', { name: /^Completed \(/ }).click();
  await expect.poll(() => pending).toBe(true);
  await page.getByRole('button', { name: /^Failed \(/ }).click();
  await expect(page).toHaveURL(/status=Failed/);
  await expect(originalRow).toBeVisible();
  hold.release();
  await expect(page.locator('[data-job-id="Failed-job"]')).toBeVisible();
  await expect(page.locator('[data-job-id="Completed-job"]')).toHaveCount(0);
  await expect(page.getByText('Loading the selected job view…')).toHaveCount(0);
});

test('real transport removes the retried dead letter and identifies the new failure', async ({
  page
}) => {
  const request = page.request;
  expect(
    (
      await request.post('/api/auth/login', {
        data: { username: 'admin', password: 'admin' }
      })
    ).ok()
  ).toBeTruthy();
  const response = await request.post('/api/queues/enqueue/exports', {
    data: {
      count: 1,
      steps: 2,
      stepDelayMs: 50,
      failTimes: 0,
      criticalFailure: true
    }
  });
  expect(response.ok()).toBeTruthy();
  const originalId = (await response.json()).jobIds[0];
  const getJob = async (id: string): Promise<JobSummary> => {
    const response = await request.get(`/api/queues/queue-job/${id}`);
    expect(response.ok()).toBeTruthy();
    return response.json();
  };
  const inspect = async (): Promise<DeadLetterView[]> => {
    const response = await request.get(
      `/api/queues/dead-letters?queueName=${queueName}&take=100`
    );
    expect(response.ok()).toBeTruthy();
    return response.json();
  };
  let retryId: string | null = null;
  try {
    await expect
      .poll(async () => (await getJob(originalId)).status)
      .toBe('Failed');
    let source: DeadLetterView | undefined;
    await expect
      .poll(async () => {
        source = (await inspect()).find(
          (letter) => letter.jobId === originalId
        );
        return !!source;
      })
      .toBe(true);
    await page.goto(`${detailUrl}?view=dead-letters`);
    const sourceRow = page.locator(`[data-message-id="${source!.messageId}"]`);
    const replayed = page.waitForResponse('**/api/queues/dead-letters/replay');
    await sourceRow.getByRole('button', { name: 'Retry message' }).click();
    const replay = await replayed;
    expect(replay.ok()).toBeTruthy();
    const result = await replay.json();
    expect(result.replayed).toBe(1);
    retryId = result.receipts[0].jobId;
    expect(retryId).not.toBe(originalId);
    await expect(sourceRow).toHaveCount(0);
    await expect(page.getByLabel('Retry result')).toContainText('Failed again');
    await page
      .getByRole('button', { name: 'Refresh dead letters', exact: true })
      .click();
    await expect(
      page.getByRole('button', { name: 'Refresh dead letters', exact: true })
    ).toBeEnabled();
    const letters = await inspect();
    expect(
      letters.some((letter) => letter.messageId === source!.messageId)
    ).toBe(false);
    const failure = letters.find((letter) => letter.jobId === retryId)!;
    expect(failure.headers['fm-original-job-id']).toBe(originalId);
    const failedRow = page.locator(`[data-message-id="${failure.messageId}"]`);
    await expect(failedRow).toContainText('Failed again after retry');
    await failedRow.getByRole('button', { name: 'Purge message' }).click();
    await page
      .getByRole('dialog', { name: 'Purge this message?' })
      .getByRole('button', { name: 'Delete message', exact: true })
      .click();
    await expect(failedRow).toHaveCount(0);
    expect((await getJob(originalId)).status).toBe('Failed');
    expect((await getJob(retryId!)).status).toBe('Failed');
  } finally {
    await page.goto('/');
    // Clean up only records created by this test, including when an assertion fails.
    for (const letter of await inspect()) {
      if (letter.jobId !== originalId && (!retryId || letter.jobId !== retryId))
        continue;
      const response = await request.post('/api/queues/dead-letters/purge', {
        data: { queueName, messageId: letter.messageId, max: 10000 }
      });
      expect(response.ok()).toBeTruthy();
    }
  }
});
