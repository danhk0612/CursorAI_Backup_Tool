#define UNICODE
#define _UNICODE

#include <windows.h>
#include <shellapi.h>
#include <string>
#include <vector>

namespace
{
    constexpr wchar_t AppFileName[] = L"CursorAI.BackupTool.App.exe";
    constexpr wchar_t RuntimeDownloadUrl[] = L"https://dotnet.microsoft.com/download/dotnet/10.0/runtime";

    std::wstring GetLauncherDirectory()
    {
        std::vector<wchar_t> buffer(32768);
        const DWORD length = GetModuleFileNameW(nullptr, buffer.data(), static_cast<DWORD>(buffer.size()));
        if (length == 0 || length >= buffer.size())
        {
            return L".";
        }

        std::wstring path(buffer.data(), length);
        const auto pos = path.find_last_of(L"\\/");
        return pos == std::wstring::npos ? L"." : path.substr(0, pos);
    }

    bool HasDesktopRuntime10()
    {
        wchar_t programFiles[32768]{};
        DWORD length = GetEnvironmentVariableW(L"ProgramFiles", programFiles, static_cast<DWORD>(std::size(programFiles)));
        if (length == 0 || length >= std::size(programFiles))
        {
            return false;
        }

        std::wstring pattern(programFiles);
        pattern += L"\\dotnet\\shared\\Microsoft.WindowsDesktop.App\\10.*";

        WIN32_FIND_DATAW data{};
        HANDLE find = FindFirstFileW(pattern.c_str(), &data);
        if (find == INVALID_HANDLE_VALUE)
        {
            return false;
        }

        bool found = false;
        do
        {
            if ((data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) != 0 &&
                data.cFileName[0] != L'.')
            {
                found = true;
                break;
            }
        }
        while (FindNextFileW(find, &data));

        FindClose(find);
        return found;
    }

    void ShowLaunchError(const wchar_t* message)
    {
        MessageBoxW(
            nullptr,
            message,
            L"CursorAI Backup Tool",
            MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    }
}

int APIENTRY wWinMain(HINSTANCE, HINSTANCE, PWSTR, int)
{
    const std::wstring directory = GetLauncherDirectory();
    const std::wstring appPath = directory + L"\\" + AppFileName;

    if (GetFileAttributesW(appPath.c_str()) == INVALID_FILE_ATTRIBUTES)
    {
        ShowLaunchError(
            L"프로그램 실행 파일을 찾을 수 없습니다.\n\n"
            L"CursorAI.BackupTool.App.exe가 CursorAI.BackupTool.exe와 같은 폴더에 있는지 확인하십시오.");
        return 1;
    }

    if (!HasDesktopRuntime10())
    {
        const int answer = MessageBoxW(
            nullptr,
            L"이 프로그램을 실행하려면 Microsoft .NET 10 Desktop Runtime (x64)이 필요합니다.\n\n"
            L"공식 다운로드 페이지를 여시겠습니까?",
            L"필요한 구성 요소가 없습니다.",
            MB_YESNO | MB_ICONINFORMATION | MB_SETFOREGROUND);

        if (answer == IDYES)
        {
            ShellExecuteW(
                nullptr,
                L"open",
                RuntimeDownloadUrl,
                nullptr,
                nullptr,
                SW_SHOWNORMAL);
        }

        return 2;
    }

    HINSTANCE result = ShellExecuteW(
        nullptr,
        L"open",
        appPath.c_str(),
        nullptr,
        directory.c_str(),
        SW_SHOWNORMAL);

    if (reinterpret_cast<INT_PTR>(result) <= 32)
    {
        ShowLaunchError(L"CursorAI Backup Tool을 시작하지 못했습니다.");
        return 3;
    }

    return 0;
}
