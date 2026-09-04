import { FetchClient } from '@foundatiofx/fetchclient';

// Always use relative URLs so the Vite dev-server proxy (or ASP.NET Core in production) handles routing.
export const api = new FetchClient({
  baseUrl: ''
});
