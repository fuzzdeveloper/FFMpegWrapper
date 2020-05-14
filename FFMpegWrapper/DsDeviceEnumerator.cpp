#include "stdafx.h"

#include <windows.h>
#include <dshow.h>

#include "DsDeviceEnumerator.h"

#pragma comment(lib, "strmiids")

HRESULT EnumerateDevices(REFGUID category, IEnumMoniker **ppEnum)
{
	// Create the System Device Enumerator.
	ICreateDevEnum *pDevEnum;
	HRESULT hr = CoCreateInstance(CLSID_SystemDeviceEnum, NULL,
		CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&pDevEnum));

	if (SUCCEEDED(hr))
	{
		// Create an enumerator for the category.
		hr = pDevEnum->CreateClassEnumerator(category, ppEnum, 0);
		if (hr == S_FALSE)
		{
			hr = VFW_E_NOT_FOUND;  // The category is empty. Treat as an error.
		}
		pDevEnum->Release();
	}
	return hr;
}

int GetDeviceInformation(IEnumMoniker *pEnum, char** videoInputDevices, int maxDevices)
{
	int count = 0;
	IMoniker *pMoniker = NULL;

	while (count < maxDevices && pEnum->Next(1, &pMoniker, NULL) == S_OK)
	{
		IPropertyBag *pPropBag;
		HRESULT hr = pMoniker->BindToStorage(0, 0, IID_PPV_ARGS(&pPropBag));
		if (FAILED(hr))
		{
			pMoniker->Release();
			continue;
		}

		VARIANT var;
		VariantInit(&var);

		// Get description or friendly name.
		hr = pPropBag->Read(L"Description", &var, 0);
		if (FAILED(hr))
		{
			hr = pPropBag->Read(L"FriendlyName", &var, 0);
		}
		if (SUCCEEDED(hr))
		{
			BSTR name = var.bstrVal;
			int l = SysStringLen(name) + 1;
			char* str = new char[l];
			sprintf_s(str, l, "%S", name);
			videoInputDevices[count++] = str;
			VariantClear(&var);
		}
		pPropBag->Release();
		pMoniker->Release();
	}
	return count;
}

int GetVideoInputDevices(char** videoInputDevices)
{
	int count = -1;
	HRESULT hr = CoInitializeEx(NULL, COINIT_MULTITHREADED);
	if (SUCCEEDED(hr) || (hr & 0x80010106) == 0x80010106)//TODO find better way to check if it's been initialised (0x80010106 means its already initialised in a different mode)
	{
		bool uninit = SUCCEEDED(hr);
		IEnumMoniker *pEnum;
		hr = EnumerateDevices(CLSID_VideoInputDeviceCategory, &pEnum);
		if (SUCCEEDED(hr))
		{
			count = GetDeviceInformation(pEnum, videoInputDevices, 1024);
			pEnum->Release();
		}
		if (uninit)
			CoUninitialize();
	}
	return count;
}

char* GetFirstVideoInputDevice()
{
	int count = -1;
	char* temp[1];
	HRESULT hr = CoInitializeEx(NULL, COINIT_MULTITHREADED);
	if (SUCCEEDED(hr) || (hr & 0x80010106) == 0x80010106)//TODO find better way to check if it's been initialised (0x80010106 means its already initialised in a different mode)
	{
		bool uninit = SUCCEEDED(hr);
		IEnumMoniker *pEnum;
		hr = EnumerateDevices(CLSID_VideoInputDeviceCategory, &pEnum);
		if (SUCCEEDED(hr))
		{
			count = GetDeviceInformation(pEnum, temp, 1);
			pEnum->Release();
		}
		if (uninit)
			CoUninitialize();
	}
	return count > 0 ? temp[0] : NULL;
}