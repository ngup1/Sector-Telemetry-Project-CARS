using System;
using System.Threading;

bool init = LogitechGSDK.LogiSteeringInitialize(false);
Console.WriteLine($"Init: {init}");

if (!init)
{
    Console.WriteLine("SDK failed to initialize.");
    return;
}

while (true)
{
    bool updated = LogitechGSDK.LogiUpdate();
    bool connected = LogitechGSDK.LogiIsConnected(0);

    if (updated && connected)
    {

        IntPtr ptr = LogitechGSDK.LogiGetStateENGINES(0);
        Console.WriteLine($"State Ptr: {ptr}");
        
        LogitechGSDK.DIJOYSTATE2ENGINES state = LogitechGSDK.LogiGetStateCSharp(0);

        Console.Clear();
        Console.WriteLine($"lX  (steering?): {state.lX}");
        Console.WriteLine($"lY  (axis):      {state.lY}");
        Console.WriteLine($"lZ  (axis):      {state.lZ}");
        Console.WriteLine($"lRz (axis):      {state.lRz}");
    }
    else
    {
        Console.Clear();
        Console.WriteLine($"Update: {updated}");
        Console.WriteLine($"Connected: {connected}");
    }

    Thread.Sleep(100);
}



