FROM mcr.microsoft.com/dotnet/core/sdk:3.1 as builder

ENV DEBIAN_FRONTEND=noninteractive \
    # prevent sending metrics to microsoft
    DOTNET_CLI_TELEMETRY_OPTOUT=1

WORKDIR /app

# install tools
COPY .config .config
RUN dotnet tool restore

# install dependencies
COPY paket.dependencies .
COPY paket.lock .
RUN dotnet paket install

# copy everything else and build
COPY build.fsx .
COPY src src
RUN dotnet fake build -t TestUnits
RUN dotnet fake build -t PublishIntegrationTests

FROM mcr.microsoft.com/dotnet/core/runtime:3.1 as runner

# install packages needed by chromedriver
RUN apt-get update \
    && apt-get -y install --no-install-recommends \
        libgdiplus \
    # clean up
    && apt-get autoremove -y \
    && apt-get clean -y \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=builder /app/src/IntegrationTests/out .

ENTRYPOINT [ "dotnet", "IntegrationTests.dll", "--headless" ]
