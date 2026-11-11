#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <vulkan/vulkan.h>

static const char *result_name(VkResult result) {
    switch (result) {
        case VK_SUCCESS: return "VK_SUCCESS";
        case VK_INCOMPLETE: return "VK_INCOMPLETE";
        case VK_ERROR_INCOMPATIBLE_DRIVER: return "VK_ERROR_INCOMPATIBLE_DRIVER";
        case VK_ERROR_INITIALIZATION_FAILED: return "VK_ERROR_INITIALIZATION_FAILED";
        case VK_ERROR_OUT_OF_HOST_MEMORY: return "VK_ERROR_OUT_OF_HOST_MEMORY";
        case VK_ERROR_OUT_OF_DEVICE_MEMORY: return "VK_ERROR_OUT_OF_DEVICE_MEMORY";
        default: return "VK_ERROR_OTHER";
    }
}

int main(void) {
    uint32_t loader_api = VK_API_VERSION_1_0;
    PFN_vkEnumerateInstanceVersion enumerate_version =
        (PFN_vkEnumerateInstanceVersion)vkGetInstanceProcAddr(VK_NULL_HANDLE, "vkEnumerateInstanceVersion");
    if (enumerate_version != NULL && enumerate_version(&loader_api) != VK_SUCCESS) {
        loader_api = VK_API_VERSION_1_0;
    }
    printf("loader_api=%u.%u.%u\n", VK_VERSION_MAJOR(loader_api),
           VK_VERSION_MINOR(loader_api), VK_VERSION_PATCH(loader_api));

    VkApplicationInfo app = {
        .sType = VK_STRUCTURE_TYPE_APPLICATION_INFO,
        .pApplicationName = "DaisyOS gaming diagnostics",
        .applicationVersion = 1,
        .pEngineName = "DaisyOS diagnostics",
        .engineVersion = 1,
        .apiVersion = VK_API_VERSION_1_0,
    };
    VkInstanceCreateInfo create = {
        .sType = VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO,
        .pApplicationInfo = &app,
    };

    VkInstance instance = VK_NULL_HANDLE;
    VkResult result = vkCreateInstance(&create, NULL, &instance);
    if (result != VK_SUCCESS) {
        printf("instance_creation=%s\n", result_name(result));
        return 2;
    }

    uint32_t count = 0;
    result = vkEnumeratePhysicalDevices(instance, &count, NULL);
    if (result != VK_SUCCESS) {
        printf("physical_device_enumeration=%s\n", result_name(result));
        vkDestroyInstance(instance, NULL);
        return 3;
    }
    printf("physical_devices=%u\n", count);
    if (count == 0) {
        vkDestroyInstance(instance, NULL);
        return 4;
    }

    VkPhysicalDevice *devices = calloc(count, sizeof(*devices));
    if (devices == NULL) {
        puts("device_list=out_of_memory");
        vkDestroyInstance(instance, NULL);
        return 5;
    }
    result = vkEnumeratePhysicalDevices(instance, &count, devices);
    if (result == VK_SUCCESS || result == VK_INCOMPLETE) {
        for (uint32_t i = 0; i < count; ++i) {
            VkPhysicalDeviceProperties properties;
            vkGetPhysicalDeviceProperties(devices[i], &properties);
            printf("device[%u]=%s;vendor=0x%04x;device=0x%04x;type=%u;driver_version=%u;api=%u.%u.%u\n",
                   i, properties.deviceName, properties.vendorID, properties.deviceID,
                   (unsigned)properties.deviceType, properties.driverVersion,
                   VK_VERSION_MAJOR(properties.apiVersion),
                   VK_VERSION_MINOR(properties.apiVersion),
                   VK_VERSION_PATCH(properties.apiVersion));
        }
    } else {
        printf("physical_device_details=%s\n", result_name(result));
        free(devices);
        vkDestroyInstance(instance, NULL);
        return 6;
    }

    free(devices);
    vkDestroyInstance(instance, NULL);
    return 0;
}
