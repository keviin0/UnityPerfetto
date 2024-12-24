namespace UnityPerfetto
{
    public static class BenchmarkConstants
    {
        public const int PLAYER_1_PID = 0;
        public const int RIGHT_EYE_PID = 1;
        public const int GAMEPLAY_EYE_PID = 2;
        public const int MILESTONES_PID = 3;
        public const int DISCREPANCY_PID = 4;

        // Number of tracked PIDs
        public const int NUMBER_OF_TRACKED_OBJECTS = 5;
        
        public static int GetPID(string category)
        {
            switch (category)
            {
                case "Player1":
                    return PLAYER_1_PID;
                default:
                    return -1;
            }
        }
    }
}
