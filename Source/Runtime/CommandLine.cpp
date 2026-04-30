#include "CommandLine.h"
#include <cstring>

namespace CommandLine
{
    const char* DebuggerAddress = "";
    bool WaitForDebugger = false;

    void Parse(int argc, const char** argv)
    {
        for (int i = 0; i < argc; ++i)
        {
            const char* arg = argv[i];

            if (std::strcmp(arg, "--debug") == 0 || std::strcmp(arg, "-debug") == 0)
            {
                // Next arg may be an explicit address, otherwise use default port
                if (i + 1 < argc && argv[i + 1][0] != '-')
                    DebuggerAddress = argv[++i];
                else
                    DebuggerAddress = "127.0.0.1:41000";
            }
            else if (std::strcmp(arg, "--debugwait") == 0 || std::strcmp(arg, "-debugwait") == 0)
            {
                WaitForDebugger = true;
            }
        }
    }

    void Parse(const char* args)
    {
        if (!args || !*args) return;

        // Build an argc/argv-style array from a space-delimited string
        const char* argv[64];
        int argc = 0;

        const char* p = args;
        while (*p && argc < 64)
        {
            while (*p == ' ') ++p;
            if (!*p) break;
            argv[argc++] = p;
            while (*p && *p != ' ') ++p;
        }

        Parse(argc, argv);
    }
}
