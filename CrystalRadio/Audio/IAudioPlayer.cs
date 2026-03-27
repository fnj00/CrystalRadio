using System.Threading.Tasks;

namespace CrystalRadio.Audio;

public interface IAudioPlayer
{
    Task<bool> PlayAsync(string streamUrl);
    void Stop();
    void Pause();
    void Resume();
    void SetVolume(float volume);

    float[] GetEqGains();
    void SetEqGain(int bandIndex, float gainDb);
    void ResetEq();
}
