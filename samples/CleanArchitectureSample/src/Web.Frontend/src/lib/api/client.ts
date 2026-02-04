import { FetchClient } from '@foundatiofx/fetchclient';

// In development, use relative URLs so Vite's proxy handles the requests
// In production (when served by ASP.NET Core), relative URLs also work
const baseUrl = import.meta.env.VITE_API_BASE_URL || '';

export const api = new FetchClient({
  baseUrl
});
