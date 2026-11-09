# Use `mise exec -- dotnet` so the pinned .NET 10 SDK is used. Override with e.g.
# `make build DOTNET=dotnet` if dotnet is already on your PATH.
DOTNET ?= mise exec -- dotnet

SOLUTION := Jellyfin.Plugin.Rewind.slnx
PROJECT := Jellyfin.Plugin.Rewind/Jellyfin.Plugin.Rewind.csproj
CONFIGURATION ?= Release
PUBLISH_DIR := build/publish

# Read from build.yaml (the Release workflow bumps it there). Stamped on the assembly too:
# Jellyfin's plugin list reports the assembly version.
VERSION := $(shell sed -n 's/^version: "\(.*\)"$$/\1/p' build.yaml)

# The release zip holds the plugin DLL only: Jellyfin already ships EF Core and SQLite, and
# loading a second copy next to it is what breaks plugins.
ZIP := build/rewind_$(VERSION).zip

.DEFAULT_GOAL := help

.PHONY: help
help: ## Show this help
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}'

.PHONY: restore
restore: ## Restore NuGet dependencies
	$(DOTNET) restore $(SOLUTION)

.PHONY: build
build: ## Build the solution
	$(DOTNET) build $(SOLUTION) -c $(CONFIGURATION)

.PHONY: test
test: ## Run the tests
	$(DOTNET) test $(SOLUTION) -c $(CONFIGURATION)

.PHONY: fmt
fmt: ## Format the code
	$(DOTNET) format $(SOLUTION)

.PHONY: check
check: build test ## Build and run the tests

.PHONY: publish
publish: ## Publish the plugin and its dependencies to build/publish
	$(DOTNET) publish $(PROJECT) -c $(CONFIGURATION) -o $(PUBLISH_DIR) -p:Version=$(VERSION) -p:AssemblyVersion=$(VERSION) -p:FileVersion=$(VERSION)

.PHONY: print-version
print-version:
	@echo $(VERSION)

.PHONY: package
package: publish ## Zip the plugin for a release (build/rewind_VERSION.zip)
	rm -f $(ZIP)
	zip -j $(ZIP) $(PUBLISH_DIR)/Jellyfin.Plugin.Rewind.dll

.PHONY: clean
clean: ## Remove build output
	$(DOTNET) clean $(SOLUTION) || true
	rm -rf build
	find . -type d -name bin -prune -exec rm -rf {} +
	find . -type d -name obj -prune -exec rm -rf {} +
