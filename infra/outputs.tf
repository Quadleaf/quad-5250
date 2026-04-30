output "AZURE_RESOURCE_GROUP" {
  value = azurerm_resource_group.this.name
}

output "AZURE_CONTAINER_REGISTRY_ENDPOINT" {
  value = azurerm_container_registry.this.login_server
}

output "AZURE_CONTAINER_REGISTRY_NAME" {
  value = azurerm_container_registry.this.name
}

output "API_INTERNAL_FQDN" {
  value = azurerm_container_app.api.ingress[0].fqdn
}

output "WEB_FQDN" {
  value = azurerm_container_app.web.ingress[0].fqdn
}

output "WEB_URL" {
  value = "https://${azurerm_container_app.web.ingress[0].fqdn}"
}
