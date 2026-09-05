namespace KPod.Core.Images;

/// <summary>Game-specific fallback palettes shared by RAW and BIN previews.</summary>
public static class BundledPalettes
{
    private static int[]? _cpr;
    private static int[]? _hellbender;
    private static int[]? _terminalVelocity;

    public static int[] MetalCr2Mtm1() => RawImageDecoder.LoadResourcePalette();
    public static int[] MetalCr2Cpr() => Clone(ref _cpr, CprBase64);
    public static int[] VgaHellbender() => Clone(ref _hellbender, HellbenderBase64);
    public static int[] VgaTerminalVelocity() => Clone(ref _terminalVelocity, TerminalVelocityBase64);

    private static int[] Clone(ref int[]? cached, string encoded)
    {
        cached ??= RawImageDecoder.DecodeAct(Convert.FromBase64String(encoded));
        return (int[])cached.Clone();
    }

    private const string CprBase64 =
        "AAAAgAAAAIAAgIAAAACAgACAAICAwMDAwNzApsrwAAAADgAABQUFCAgICwoKABQUCAg/KQUBEBAQCgpPAAC/DAxeBh4UGBgXGRgYDg5uGRkZQggFMwg/EBB9HRwaDCgUTwoKEhKOISEhIyEeQwpPFBSeUREOXgwKEjIUKiUlFhavKCgjCD8IKSkoUgxeGBi/bg4OLS0mGDwUYRkQGhrPNS0sfBAPMTExHBzfMjMqYg5uCk8KOzIwHkYVHh7vcCERNzkujhISICD/Pz8IQTY0cRB9vwAAOjo6Oj4xlRgGI1AVDF4MnhQURzs4ghKOQkJCTT46Pkc2gS0WKVoVrxcSDm4OU0M+T08KGFpzkhSeQVA7SkpKvxgYL2QVWkhBkTUYvwC/Q1g/zxoaEH0QoxavyyEJUlJSYU1GNW4VXl4MSWBFoDwZ3xwcsxi/Wlpaa1VMEo4SIXOEUGpLsUIb9R8fwRrPTU3/dFtRY2Njbm4O0j0MFJ4UVXJRv0wef2NWa2trUIYnWntWKYyMFq8WAL8AfX0Qimtbc3NzYINc11YTlHJfGL8Ye3t7MZycZo1ijo4Sn3lk/01Nap45hISEOaWlGs8a9GIVAL+/bplqeXn/33QTp4NsjIyMQq2t70j/np4UHN8crYt0lJSUfaZ6/3cjHu8eSr21tJV9nJyc5ZAWhbdLr68Wi7KJ/3p6Usa9u52GI/8jpaWlv78AWs7GwaWOv78Yra2tmLyX+Hn+/5hP7KwZpqb/Y9bKyK6Xn89cocKhtbW1z88aqcipzrihc97Ovb29rcytwMDA/6ampMjw8sgd1L+pxcXF/7l6ev96398c/6b+2sezgu3duuduzs7OwNzAhPfn+eMg1tbW7+8e3t7e/9qmnP/31P+A5ubm//8g//9N7+/v//95//+m9/f3////AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA//vwoKCkgICA/wAAAP8A//8AAAD//wD/AP//////";

    private const string HellbenderBase64 =
        "AAAACAgIEBAQGRkZISEhKSkpMTExOjo6QkJCSkpKUlJSWlpaY2Nja2trc3Nze3t7hISEjIyMlJSUnJycpaWlra2ttbW1vb29xcXFzs7O1tbW3t7e5ubm7+/v9/f3////AxADBRgFByAHCSgJCzAMDjcQED8TFEUWFksaGlEeHlciIl0oJ2IsK2gxMG03NnM/QXtJS4NTVoxdYJRna5xxcaB3eaZ/gayGibKOkbiVmr6dosOkqsmsss+zutW7wtvCBgYGCwoKEA8PFBMTGRgYHxwcJSEgKiUlLykoNS0sOzIwQTY0Rzs4TT46U0M+WkhBYU1Ga1VMdFtRf2NWimtblHJfn3lkp4NsrYt0tJV9u52GwaWOyK6Xzrih1L+p2sezCAhABwdbBgZ3BQWSAwOtAgLIAQHkAAD/HR3/Ojr/V1f/dHT/kZH/rq7/y8v/6Oj/CEAIB1sHBncGBZIFA60DAsgCAeQBAP8AHf8dOv86V/9XdP90kf+Rrv+uy//L6P/oDgAAKQUBRAkDXw4EehMFlRgGsBwIyyEJ0j0M2FkQ33QT5ZAW7KwZ8sgd+eMg//8jWRAGbBMHfhYIkRkIoxwJth4KyCEL2yQL7ScM8EAp8llG9XJj94yA+qWd/L66/9fXNw4EURgGbCQHiDMJokELvFQN1mkP8YEQ9Jkr9a5G98Fg+NF7+t2V/Oqx/vTL//3oNQwIQxcLUSIOXy4SbTkVe0QYiFEelV0joWoprnYuu4M0x5A7055C36tK67lR98ZYIgUiJgU1KwRILwRbNANuOAOBPQKUQQKnWRCycR69iCzHoDrSuEfd0FXo52Py/3H9ABQUBh4UDCgUEjIUGDwUHkYVI1AVKVoVL2QVNW4VUIYnap45hbdLn89cuudu1P+AGFpzIXOEKYyMMZycOaWlQq2tSr21Usa9Ws7GY9bGY9bOc97Oe+fehO/ehPfnnP/3AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private const string TerminalVelocityBase64 =
        "AAAABwcHDg4OFhYWHR0dJCQkKysrMzMzOjo6QUFBSEhIT09PV1dXXl5eZWVlbGxsdHR0e3t7goKCiYmJkZGRmJiYn5+fpqamra2ttbW1vLy8w8PDysrK0tLS2dnZ4ODgBQUFCgkJDg0NExISGBgXHRwaIyEeKCgjLS0mMjMqNzkuOj4xPUQ1QEs4QVA7Q1g/SWBFUGpLVXJRWntWYINcZo1ibJZncZ1ueaN2gqp/ibCHkbaPmLyXocKhqciprcytBgYGCwoKEA8PFBMTGRgYHxwcJSEgKiUlLykoNS0sOzIwQTY0Rzs4TT46U0M+WkhBYU1Ga1VMdFtRf2NWimtblHJfn3lkp4NsrYt0tJV9u52GwaWOyK6Xzrih1L+p2sezAwMQBQUcCAgnCgozDg08EhFGFxRRHBhbIh1kJyJtLid2NCx/PDKFQjiNSz6VU0mcV02kW1CrYVewa1+0cme2em+6gXe8iYC/kIjDl4/Gn5jJp6DNrqfQta/Uu7bXw77bDgAAGwAAKAABNQABQgABTwABXAACaQACdgACgwACkAADnQADqgADtwADxAAExw8HyyEJ0DQL1EYN2FkQ3WsS4X4U5ZAW6aMY7rUa8sgd9tof++0h//8j//9s//+2////CAggEBBAGBhgICCAKCigMDDAODjgPz//CCAgEEBAGGBgIICAKKCgMMDAOODgP///OA8FRhYHVR8KYikNbzQQfUEUiU4YlVscomghrXYmt4Usw5Yyy6U6zLBJzbpaz8JpAgIlCgUsEgkzGgw6IxBBKxNIMxdPOxpWQx1dSyFkVCRrXChyZCt5bC6AdDKHfDWOhTmUjTyblUCinUOppUawrUq3tk2+vlHFxlTMzlfT1lva3l7h52Lo72Xv92n2/2z9Nw4EURgGbCQHiDMJokELvFQN1mkP8YEQ9Jkr9a5G98Fg+NF7+t2V/Oqx/vTL//3oAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
}
