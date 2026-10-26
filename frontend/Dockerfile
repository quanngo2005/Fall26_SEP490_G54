FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build -- --configuration production

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
COPY scripts/replace_env.sh /docker-entrypoint.d/40-replace-env.sh
# Strip CRLF in case the script was checked out on Windows with autocrlf.
RUN rm /etc/nginx/conf.d/default.conf \
    && sed -i 's/\r$//' /docker-entrypoint.d/40-replace-env.sh \
    && chmod +x /docker-entrypoint.d/40-replace-env.sh
EXPOSE 80
