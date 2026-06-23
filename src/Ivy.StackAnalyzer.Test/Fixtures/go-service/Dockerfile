FROM golang:1.22 AS build
WORKDIR /src
COPY . .
RUN go build -o /svc

FROM gcr.io/distroless/base
COPY --from=build /svc /svc
ENTRYPOINT ["/svc"]
