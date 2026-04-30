variable "environment_name" {
  type        = string
  description = "azd environment name. Used in resource names and as the environment tag."
}

variable "location" {
  type        = string
  description = "Azure region for all resources."
}

variable "prefix" {
  type        = string
  default     = "quad5250"
  description = "Resource name prefix."
}

variable "api_image" {
  type        = string
  default     = "mcr.microsoft.com/azuredocs/containerapps-helloworld:latest"
  description = "API container image. azd overrides this on each deploy."
}

variable "web_image" {
  type        = string
  default     = "mcr.microsoft.com/azuredocs/containerapps-helloworld:latest"
  description = "Web container image. azd overrides this on each deploy."
}
