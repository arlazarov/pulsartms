namespace Application.Models;

// A request whose length is a wait it was built to make - a long poll that
// answers when something changes - rather than work. Its time is still
// recorded with the others, but it is never logged as slow: an idle
// browser answered every 20 seconds would otherwise be a log line every 20
// seconds.
public interface IWaitsByDesign;
