import { useCallback, useEffect, useRef, useState, type DragEvent } from 'react';
import { CheckCircle2, Download, FileText, FileWarning, History, ShieldCheck, Smartphone, UploadCloud, XCircle } from 'lucide-react';
import {
  apiErrorMessage,
  approveMkobaImport,
  downloadMkobaImport,
  getMkobaImport,
  listMkobaImports,
  rejectMkobaImport,
  stageMkobaPdf,
  type ImportReport,
  type StagedImportDetail,
  type StagedImportSummary,
} from '../../api/truth';
import { Button, Card, DecisionModal, Notice, Stat } from '../../components/ui';
import { StatusBadge } from '../../components/StatusBadge';
import { GapChain } from '../../components/truth/GapChain';
import { useToast } from '../../lib/toast';
import { saveBlob } from '../../lib/benefits';
import { num, realDateTime, requestStatus, wallDate, wallDateTime } from '../../lib/truth';
import { useTruth } from './TruthContext';
import { locMsg } from '../../lib/loc';

import { t } from '../../i18n';
/**
 * SASISHO17 (P0-2): PDF ya M-Koba ndiyo chanzo kikuu cha ukweli wa fedha, kwa hiyo haiingii moja kwa moja.
 *   1. Kiongozi A anapakia → RASIMU (inakaguliwa: mipya/iliyopo/migongano/ya nyuma/mapengo/namba zisizojulikana).
 *   2. Kiongozi B (si A) anapitia rasimu, anaweza kupakua PDF yenyewe, kisha anaidhinisha au anakataa kwa sababu.
 * Server ndiyo inayozuia (A hawezi kuidhinisha yake); ukurasa huu unaonyesha hali tu.
 */
export function ImportTab() {
  const { groupId, refresh, goTab } = useTruth();
  const toast = useToast();
  const input = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [drag, setDrag] = useState(false);
  const [busy, setBusy] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [list, setList] = useState<StagedImportSummary[] | null>(null);
  const [pending, setPending] = useState<StagedImportDetail | null>(null);
  const [report, setReport] = useState<ImportReport | null>(null);

  const [reload, setReload] = useState(0);
  const load = useCallback(() => setReload((v) => v + 1), []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const l = await listMkobaImports(groupId);
        const p = l.find((x) => x.status === 'Pending');
        const detail = p ? await getMkobaImport(groupId, p.id) : null;
        if (cancelled) return;
        setList(l);
        setPending(detail);
      } catch (e) {
        if (!cancelled) setErr(apiErrorMessage(e));
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [groupId, reload]);

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
      const d = await stageMkobaPdf(groupId, file);
      setPending(d);
      setFile(null);
      toast(t("Rasimu imehifadhiwa: miamala mipya {0}. Kiongozi mwingine aipitie na kuiidhinisha.", { 0: d.newRows }), 'good');
      load();
    } catch (e) {
      setErr(apiErrorMessage(e, t('Imeshindwa kupakia PDF.')));
    } finally {
      setBusy(false);
    }
  }

  const onDrop = (e: DragEvent) => {
    e.preventDefault();
    setDrag(false);
    pick(e.dataTransfer.files?.[0]);
  };

  return (
    <div className="space-y-5">
      {pending ? (
        <StagedDraft
          groupId={groupId}
          draft={pending}
          onApproved={(r) => {
            setReport(r);
            setPending(null);
            refresh();
            load();
          }}
          onClosed={() => {
            setPending(null);
            load();
          }}
        />
      ) : (
        <Card title={t("Pakia PDF ya M-Koba")} subtitle={t("Group Statement kutoka Vodacom M-Koba. Itakuwa RASIMU mpaka kiongozi mwingine aiidhinishe.")}>
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
            <div className="mt-1 text-xs text-ink-600">{file ? `${(file.size / 1024).toFixed(0)} KB` : t("PDF pekee")}</div>
            <input ref={input} type="file" accept="application/pdf,.pdf" className="hidden" onChange={(e) => pick(e.target.files?.[0])} />
          </div>
          <div className="mt-4 flex flex-wrap items-center gap-3">
            <Button onClick={upload} disabled={!file} loading={busy} icon={UploadCloud}>
              {t("Pakia kama rasimu")}
            </Button>
            <span className="text-xs text-ink-600">{t("Miamala iliyopo haiongezwi mara mbili. Rejea iliyopo yenye data tofauti inakataliwa na kurekodiwa.")}</span>
          </div>
        </Card>
      )}

      {err && <Notice tone="overdue">{err}</Notice>}

      {report && <ImportResult report={report} goTab={goTab} />}

      {list && list.length > 0 && (
        <Card title={<span className="inline-flex items-center gap-2"><History size={16} />{t("Historia ya PDF za M-Koba")}</span>}>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-ink-100 text-left text-xs text-ink-600">
                  <th className="py-2 pr-3">{t("Imepakiwa")}</th>
                  <th className="py-2 pr-3">{t("Kipindi")}</th>
                  <th className="py-2 pr-3 text-right">{t("Mipya")}</th>
                  <th className="py-2 pr-3 text-right">{t("Salio la mwisho")}</th>
                  <th className="py-2 pr-3">{t("Hali")}</th>
                  <th className="py-2" />
                </tr>
              </thead>
              <tbody>
                {list.map((i) => {
                  const st = requestStatus(i.status);
                  return (
                    <tr key={i.id} className="border-b border-ink-100 last:border-0">
                      <td className="py-2 pr-3">{realDateTime(i.uploadedAt)}{i.mine && <span className="ml-1 text-xs text-ink-600">({t("wewe")})</span>}</td>
                      <td className="py-2 pr-3">{wallDate(i.periodFrom)} – {wallDate(i.periodTo)}</td>
                      <td className="figure py-2 pr-3 text-right">{i.newRows}</td>
                      <td className="figure py-2 pr-3 text-right">{num(i.lastBalance)}</td>
                      <td className="py-2 pr-3"><StatusBadge label={st.label} tone={st.tone} /></td>
                      <td className="py-2 text-right">
                        <Button size="sm" variant="secondary" icon={Download} onClick={async () => {
                          try {
                            saveBlob(await downloadMkobaImport(groupId, i.id), `mkoba-${i.id.slice(0, 8)}.pdf`);
                          } catch (e) {
                            toast(apiErrorMessage(e), 'overdue');
                          }
                        }}>
                          PDF
                        </Button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </Card>
      )}
    </div>
  );
}

/** Rasimu inayosubiri: kiongozi wa pili anaipitia hapa. */
function StagedDraft({
  groupId,
  draft,
  onApproved,
  onClosed,
}: {
  groupId: string;
  draft: StagedImportDetail;
  onApproved: (r: ImportReport) => void;
  onClosed: () => void;
}) {
  const toast = useToast();
  const [modal, setModal] = useState<'approve' | 'reject' | null>(null);
  const [showRows, setShowRows] = useState(false);
  const [busy, setBusy] = useState(false);
  const r = draft.report;
  const balanceOk = r.declaredGroupBalance != null && draft.lastBalance != null ? r.declaredGroupBalance === draft.lastBalance : null;

  return (
    <Card
      title={<span className="inline-flex items-center gap-2"><ShieldCheck size={16} />{t("Rasimu ya PDF inasubiri idhini")}</span>}
      subtitle={t("Imepakiwa {0} · {1}", { 0: realDateTime(draft.uploadedAt), 1: draft.fileName ?? 'statement.pdf' })}
      action={
        <Button size="sm" variant="secondary" icon={Download} onClick={async () => {
          try {
            saveBlob(await downloadMkobaImport(groupId, draft.id), draft.fileName ?? 'mkoba.pdf');
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}>
          {t("Pakua PDF")}
        </Button>
      }
    >
      <div className="space-y-4">
        {draft.mine ? (
          <Notice tone="warn">{t("Wewe ndiye uliyepakia rasimu hii. Kiongozi MWINGINE lazima aipitie na kuiidhinisha; wewe unaweza kuighairi tu.")}</Notice>
        ) : (
          <Notice tone="neutral">{t("Linganisha takwimu hizi na PDF yenyewe (pakua) kabla ya kuidhinisha. Ukiidhinisha, miamala inaingia kwenye taarifa za wanachama wote mara moja.")}</Notice>
        )}
        <div className="grid grid-cols-2 gap-2.5 lg:grid-cols-4">
          <Stat label={t("Mistari kwenye PDF")} value={draft.rowsInPdf} />
          <Stat label={t("Mipya")} value={draft.newRows} tone="good" hint={t("{0} ilikuwepo", { 0: draft.existingRows })} />
          <Stat label={t("Kipindi")} value={`${wallDate(draft.periodFrom)} – ${wallDate(draft.periodTo)}`} />
          <Stat label={t("Salio la mwisho")} value={num(draft.lastBalance)} />
        </div>
        <ul className="space-y-2 text-sm">
          <Check ok={balanceOk} label={t("Balance ya mwisho ya mistari = Group Balance ya PDF")} detail={`${num(draft.lastBalance)} / ${num(r.declaredGroupBalance)}`} />
          <Check ok={r.unreadableRows.length === 0} label={t("Mistari yote imesomeka")} detail={r.unreadableRows.length ? t('{0} haikusomeka', { 0: r.unreadableRows.length }) : undefined} />
          <Check ok={draft.backdatedRows === 0} label={t("Hakuna miamala mipya yenye tarehe ya nyuma")} detail={draft.backdatedRows ? String(draft.backdatedRows) : undefined} />
          <Check ok={r.gaps.newGaps.length === 0} label={t("Hakuna mapengo mapya ya balance")} detail={r.gaps.newGaps.length ? t('mapengo {0}', { 0: r.gaps.newGaps.length }) : undefined} />
          <Check ok={r.unknownPhones.length === 0} label={t("Namba zote zimeunganishwa na wanachama")} detail={r.unknownPhones.length ? t('{0} hazijulikani', { 0: r.unknownPhones.length }) : undefined} />
        </ul>
        {r.gaps.closedGaps.length > 0 && <Notice tone="good">{t("PDF hii inaziba mapengo {0} yaliyokuwepo.", { 0: r.gaps.closedGaps.length })}</Notice>}
        {r.warnings.map((w, i) => (
          <Notice key={i} tone="warn">{locMsg(r.warningsLoc?.[i], w)}</Notice>
        ))}
        {r.unknownPhones.length > 0 && (
          <ul className="grid gap-2 text-sm sm:grid-cols-2">
            {r.unknownPhones.map((p) => (
              <li key={p.phone} className="rounded-lg bg-ink-50 px-3 py-2">
                <span className="figure font-medium">{p.phone}</span> <span className="text-ink-600">· {p.mkobaName ?? '—'} · {num(p.total)}</span>
              </li>
            ))}
          </ul>
        )}
        <div>
          <Button size="sm" variant="secondary" onClick={() => setShowRows((v) => !v)}>
            {showRows ? t("Ficha miamala mipya") : t("Onyesha miamala mipya ({0})", { 0: draft.rows.length })}
          </Button>
          {showRows && (
            <div className="mt-3 max-h-96 overflow-auto rounded-lg border border-ink-100">
              <table className="w-full text-xs">
                <thead className="sticky top-0 bg-paper-raised">
                  <tr className="border-b border-ink-100 text-left text-ink-600">
                    <th className="px-2 py-1.5">{t("Tarehe")}</th>
                    <th className="px-2 py-1.5">{t("Rejea")}</th>
                    <th className="px-2 py-1.5">{t("Namba")}</th>
                    <th className="px-2 py-1.5">{t("Jina (M-Koba)")}</th>
                    <th className="px-2 py-1.5 text-right">{t("Kiasi")}</th>
                    <th className="px-2 py-1.5 text-right">{t("Salio")}</th>
                  </tr>
                </thead>
                <tbody>
                  {draft.rows.map((x) => (
                    <tr key={x.reference} className="border-b border-ink-100 last:border-0">
                      <td className="px-2 py-1.5">{wallDateTime(x.timestamp)}</td>
                      <td className="figure px-2 py-1.5">{x.reference}</td>
                      <td className="figure px-2 py-1.5">{x.phone}</td>
                      <td className="px-2 py-1.5">{x.payerName}</td>
                      <td className={`figure px-2 py-1.5 text-right ${x.isWithdrawal ? 'text-standing-overdue' : ''}`}>{x.isWithdrawal ? '−' : ''}{num(x.amount)}</td>
                      <td className="figure px-2 py-1.5 text-right">{num(x.balance)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
        <div className="flex flex-wrap justify-end gap-2 border-t border-ink-100 pt-3">
          {draft.canCancel && (
            <Button variant="secondary" icon={XCircle} loading={busy} onClick={async () => {
              setBusy(true);
              try {
                await rejectMkobaImport(groupId, draft.id);
                toast(t("Rasimu imeghairiwa."), 'neutral');
                onClosed();
              } catch (e) {
                toast(apiErrorMessage(e), 'overdue');
              } finally {
                setBusy(false);
              }
            }}>
              {t("Ghairi rasimu")}
            </Button>
          )}
          {draft.canApprove && (
            <>
              <Button variant="danger" icon={XCircle} onClick={() => setModal('reject')}>{t("Kataa")}</Button>
              <Button variant="good" icon={CheckCircle2} onClick={() => setModal('approve')}>{t("Idhinisha na uingize")}</Button>
            </>
          )}
        </div>
      </div>
      <DecisionModal
        open={modal !== null}
        approve={modal === 'approve'}
        onClose={() => setModal(null)}
        summary={t("PDF ya M-Koba: miamala mipya {0}, kipindi {1} – {2}, salio la mwisho {3}.", {
          0: draft.newRows, 1: wallDate(draft.periodFrom), 2: wallDate(draft.periodTo), 3: num(draft.lastBalance),
        })}
        onConfirm={async (note) => {
          try {
            if (modal === 'approve') {
              const res = await approveMkobaImport(groupId, draft.id, note);
              toast(t("Imeingizwa: miamala mipya {0}, iliyokuwepo {1}.", { 0: res.report.inserted, 1: res.report.alreadyExisted }), 'good');
              setModal(null);
              onApproved(res.report);
            } else {
              await rejectMkobaImport(groupId, draft.id, note);
              toast(t("Rasimu imekataliwa."), 'neutral');
              setModal(null);
              onClosed();
            }
          } catch (e) {
            toast(apiErrorMessage(e), 'overdue');
          }
        }}
      />
    </Card>
  );
}

/** Matokeo baada ya idhini (ripoti kamili ya import). */
function ImportResult({ report, goTab }: { report: ImportReport; goTab: (tab: string) => void }) {
  const balanceOk = report.declaredGroupBalance != null && report.lastBalanceInPdf != null ? report.declaredGroupBalance === report.lastBalanceInPdf : null;
  return (
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
                    {locMsg(report.warningsLoc?.[i], w)}
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
