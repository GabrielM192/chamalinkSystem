import { useRef, useState, type DragEvent } from 'react';
import { CheckCircle2, FileText, FileWarning, Smartphone, UploadCloud } from 'lucide-react';
import { apiErrorMessage, importMkobaPdf, type ImportReport } from '../../api/truth';
import { Button, Card, Notice, Stat } from '../../components/ui';
import { GapChain } from '../../components/truth/GapChain';
import { useToast } from '../../lib/toast';
import { num } from '../../lib/truth';
import { useTruth } from './TruthContext';

import { t } from '../../i18n';
/**
 * Import ya PDF ya M-Koba: inahifadhi miamala GHAFI tu (hakuna mgawanyo).
 * Ni salama kurudia — miamala iliyopo inarukwa. Taarifa zinahesabiwa upya papo hapo.
 */
export function ImportTab() {
  const { groupId, refresh, goTab } = useTruth();
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [drag, setDrag] = useState(false);
  const [busy, setBusy] = useState(false);
  const [report, setReport] = useState<ImportReport | null>(null);
  const [err, setErr] = useState<string | null>(null);

  function pick(f: File | undefined | null) {
    setErr(null);
    setReport(null);
    if (!f) return;
    if (!f.name.toLowerCase().endsWith('.pdf')) {
      setErr(t("Chagua faili la PDF la M-Koba (Group Statement)."));
      return;
    }
    setFile(f);
  }

  async function upload() {
    if (!file) return;
    setBusy(true);
    setErr(null);
    try {
      const r = await importMkobaPdf(groupId, file);
      setReport(r);
      toast(t("Imeingizwa: miamala mipya {0}, iliyokuwepo {1}.", { 0: r.inserted, 1: r.alreadyExisted }), 'good');
      refresh();
    } catch (e) {
      setErr(apiErrorMessage(e, t('Imeshindwa kuingiza PDF.')));
    } finally {
      setBusy(false);
    }
  }

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDrag(false);
    pick(e.dataTransfer.files?.[0]);
  };

  const balanceOk = report && report.declaredGroupBalance != null && report.lastBalanceInPdf != null ? report.declaredGroupBalance === report.lastBalanceInPdf : null;

  return (
    <div className="space-y-5">
      <Card title={t("Pakia PDF ya M-Koba")} subtitle={t("Group Statement kutoka Vodacom M-Koba. Unaweza kupakia PDF zinazofuatana (mf. Jan–Ago, kisha Ago–Sep).")}>
        <div
          onDragOver={(e) => {
            e.preventDefault();
            setDrag(true);
          }}
          onDragLeave={() => setDrag(false)}
          onDrop={onDrop}
          onClick={() => input.current?.click()}
          role="button"
          tabIndex={0}
          onKeyDown={(e) => e.key === 'Enter' && input.current?.click()}
          className={`flex cursor-pointer flex-col items-center justify-center rounded-2xl border-2 border-dashed px-6 py-10 text-center transition-colors ${
            drag ? 'border-gold-600 bg-gold-100/40' : 'border-ink-100 bg-paper-raised hover:border-ink-500'
          }`}
        >
          {file ? <FileText size={30} className="text-ink-700" /> : <UploadCloud size={30} className="text-ink-500" />}
          <div className="mt-3 font-medium text-ink-950">{file ? file.name : t("Buruta PDF hapa au bofya kuchagua")}</div>
          <div className="mt-1 text-xs text-ink-600">{file ? `${(file.size / 1024).toFixed(0)} KB` : t("PDF pekee · hadi 20 MB")}</div>
          <input ref={input} type="file" accept="application/pdf,.pdf" className="hidden" onChange={(e) => pick(e.target.files?.[0])} />
        </div>
        <div className="mt-4 flex flex-wrap items-center gap-3">
          <Button onClick={upload} disabled={!file} loading={busy} icon={UploadCloud}>
            {t("Ingiza")}
          </Button>
          <span className="text-xs text-ink-600">{t("Ni salama kurudia PDF ileile — miamala iliyopo haitaongezwa mara mbili.")}</span>
        </div>
        {err && (
          <div className="mt-4">
            <Notice tone="overdue">{err}</Notice>
          </div>
        )}
      </Card>

      {report && (
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
            <Stat label={t("Mistari kwenye PDF")} value={report.rowsInPdf} />
            <Stat label={t("Mipya")} value={report.inserted} tone="good" hint={t("{0} ilikuwepo", { 0: report.alreadyExisted })} />
            <Stat label={t("Deposits")} value={num(report.depositsInPdf)} />
            <Stat label={t("Withdrawals")} value={num(report.withdrawalsInPdf)} />
          </div>

          <Card title={t("Ukaguzi wa uadilifu")}>
            <ul className="space-y-2 text-sm">
              <Check ok={balanceOk} label={t("Balance ya mwisho ya mistari = Group Balance ya PDF")} detail={`${num(report.lastBalanceInPdf)} / ${num(report.declaredGroupBalance)}`} />
              <Check ok={report.unreadableRows.length === 0} label={t("Mistari yote imesomeka")} detail={report.unreadableRows.length ? t('{0} haikusomeka', { 0: report.unreadableRows.length }) : undefined} />
              <Check ok={report.gaps.length === 0} label={t("Mnyororo wa balance hauna mapengo")} detail={report.gaps.length ? t('mapengo {0}', { 0: report.gaps.length }) : undefined} />
              <Check ok={report.unknownPhones.length === 0} label={t("Namba zote zimeunganishwa na wanachama")} detail={report.unknownPhones.length ? t('{0} hazijulikani', { 0: report.unknownPhones.length }) : undefined} />
            </ul>
            {report.warnings.length > 0 && (
              <div className="mt-4 space-y-2">
                {report.warnings.map((w, i) => (
                  <Notice key={i} tone="warn">
                    {w}
                  </Notice>
                ))}
              </div>
            )}
          </Card>

          {report.gaps.length > 0 && (
            <Card
              title={t("Balance imeruka — mapengo {0}", { 0: report.gaps.length })}
              action={
                <Button size="sm" variant="secondary" icon={FileWarning} onClick={() => goTab('mapengo')}>
                  {t("Yatatue")}
                </Button>
              }
            >
              <div className="grid gap-3 md:grid-cols-2">
                {report.gaps.map((g) => (
                  <div key={g.id} className="rounded-xl border border-ink-100 bg-paper-raised p-3">
                    <div className="mb-2 flex items-start justify-between gap-2">
                      <p className="text-sm font-medium text-ink-900">{g.title}</p>
                      {g.resolved && <span className="shrink-0 text-xs text-standing-good">{t("imethibitishwa")}</span>}
                    </div>
                    <GapChain gap={g} amount={g.amount} />
                    <p className="mt-2 text-xs leading-relaxed text-ink-700">{g.maelezo}</p>
                  </div>
                ))}
              </div>
            </Card>
          )}

          {report.unknownPhones.length > 0 && (
            <Card
              title={t("Namba zisizojulikana")}
              action={
                <Button size="sm" variant="secondary" icon={Smartphone} onClick={() => goTab('simu')}>
                  {t("Ziunganishe")}
                </Button>
              }
            >
              <ul className="grid gap-2 text-sm sm:grid-cols-2">
                {report.unknownPhones.map((p) => (
                  <li key={p.phone} className="rounded-lg bg-ink-50 px-3 py-2">
                    <span className="figure font-medium">{p.phone}</span> <span className="text-ink-600">· {p.mkobaName ?? '—'}</span>
                  </li>
                ))}
              </ul>
            </Card>
          )}

          {report.unreadableRows.length > 0 && (
            <Card title={t("Mistari isiyosomeka")}>
              <pre className="max-h-60 overflow-auto whitespace-pre-wrap rounded-lg bg-ink-50 p-3 text-xs">{report.unreadableRows.join('\n')}</pre>
            </Card>
          )}
        </div>
      )}
    </div>
  );
}

function Check({ ok, label, detail }: { ok: boolean | null; label: string; detail?: string }) {
  return (
    <li className="flex items-start gap-2">
      {ok === null ? (
        <span className="mt-0.5 h-4 w-4 rounded-full bg-ink-100" />
      ) : ok ? (
        <CheckCircle2 size={16} className="mt-0.5 text-standing-good" />
      ) : (
        <FileWarning size={16} className="mt-0.5 text-standing-warn" />
      )}
      <span className="flex-1">{label}</span>
      {detail && <span className="figure text-xs text-ink-600">{detail}</span>}
    </li>
  );
}
