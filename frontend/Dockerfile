# Multi-stage build — matches the Dockerfile pattern used across the portfolio's
# other projects (build stage compiles, a small runtime stage serves the result).
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .

# Vite inlines import.meta.env at build time, so these must be present before
# `npm run build` runs — compose.yaml passes them as build args.
ARG VITE_API_URL
ARG VITE_API_MOCKING
ENV VITE_API_URL=$VITE_API_URL
ENV VITE_API_MOCKING=$VITE_API_MOCKING
RUN npm run build

# Unprivileged variant runs as non-root by default and already listens on the
# unprivileged port 8080 that nginx.conf uses.
FROM nginxinc/nginx-unprivileged:1.27-alpine AS runtime
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
