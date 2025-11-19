# Use official .NET SDK image for build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY . .
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Use official .NET runtime image for run
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./
COPY .env .env

# Optionally, set default environment variables (can be overridden at runtime)
ENV AZURE_OPENAI_ENDPOINT=""
ENV AZURE_OPENAI_KEY=""
ENV AZURE_OPENAI_MODEL="model-router"
ENV EVENTHUB_CONNECTION_STRING=""
ENV EVENTHUB_NAME=""
ENV ConnectionStrings__DefaultConnection="Server=yourserver;Database=yourdb;User Id=youruser;Password=yourpassword;"

# Expose port (change if your app uses a different port)
EXPOSE 80

ENTRYPOINT ["dotnet", "RetailMonolith.dll"]
